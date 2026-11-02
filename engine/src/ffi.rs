use crate::discovery::DiscoveryManager;
use crate::protocol::{FrameHeader, FrameType};
use crate::session::SessionManager;
use crate::transport::{read_frame, write_frame};
use serde::{Deserialize, Serialize};
use std::cell::RefCell;
use std::ffi::{CStr, CString};
use std::os::raw::{c_char, c_void};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, OnceLock};
use tokio::net::{TcpListener, TcpStream};
use tokio::runtime::Runtime;

pub type EventCallback =
    extern "C" fn(event_type: i32, payload_json: *const c_char, user_data: *mut c_void);

pub const DZ_EVENT_SERVER_STARTED: i32 = 1;
pub const DZ_EVENT_PEER_DISCOVERED: i32 = 2;
pub const DZ_EVENT_TRANSFER_REQUEST: i32 = 10;
pub const DZ_EVENT_TRANSFER_ACCEPTED: i32 = 11;
pub const DZ_EVENT_TRANSFER_REJECTED: i32 = 12;
pub const DZ_EVENT_PROGRESS: i32 = 20;
pub const DZ_EVENT_PAUSED: i32 = 30;
pub const DZ_EVENT_RESUMED: i32 = 31;
pub const DZ_EVENT_CANCELLED: i32 = 32;
pub const DZ_EVENT_COMPLETED: i32 = 40;
pub const DZ_EVENT_ERROR: i32 = 99;

#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct EngineInitConfig {
    pub device_id: String,
    pub device_name: String,
    pub download_directory: String,
    pub listen_port: u16,
    pub auto_accept_trusted: bool,
}

struct EngineState {
    runtime: Runtime,
    config: Mutex<Option<EngineInitConfig>>,
    callback: Mutex<Option<(EventCallback, *mut c_void)>>,
    discovery: DiscoveryManager,
    sessions: SessionManager,
    server_running: Arc<AtomicBool>,
}

unsafe impl Send for EngineState {}
unsafe impl Sync for EngineState {}

static ENGINE: OnceLock<EngineState> = OnceLock::new();

thread_local! {
    static LAST_ERROR: RefCell<CString> = RefCell::new(CString::new("").unwrap());
}

fn set_last_error(msg: &str) {
    LAST_ERROR.with(|cell| {
        *cell.borrow_mut() = CString::new(msg).unwrap_or_default();
    });
}

fn get_state() -> Option<&'static EngineState> {
    ENGINE.get()
}

fn dispatch_event(event_type: i32, json_str: &str) {
    if let Some(state) = get_state() {
        let cb_guard = state.callback.lock().unwrap();
        if let Some((cb, user_data)) = *cb_guard {
            if let Ok(c_str) = CString::new(json_str) {
                cb(event_type, c_str.as_ptr(), user_data);
            }
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_init(config_json: *const c_char) -> i32 {
    if config_json.is_null() {
        set_last_error("Config JSON pointer is null");
        return -1;
    }

    let c_str = CStr::from_ptr(config_json);
    let str_slice = match c_str.to_str() {
        Ok(s) => s,
        Err(_) => {
            set_last_error("Invalid UTF-8 in config JSON");
            return -2;
        }
    };

    let config: EngineInitConfig = match serde_json::from_str(str_slice) {
        Ok(c) => c,
        Err(e) => {
            set_last_error(&format!("JSON deserialize error: {}", e));
            return -3;
        }
    };

    let rt = match Runtime::new() {
        Ok(r) => r,
        Err(e) => {
            set_last_error(&format!("Failed to create Tokio runtime: {}", e));
            return -4;
        }
    };

    let state = EngineState {
        runtime: rt,
        config: Mutex::new(Some(config.clone())),
        callback: Mutex::new(None),
        discovery: DiscoveryManager::new(),
        sessions: SessionManager::new(),
        server_running: Arc::new(AtomicBool::new(false)),
    };

    if ENGINE.set(state).is_err() {
        if let Some(s) = ENGINE.get() {
            let mut cfg = s.config.lock().unwrap();
            *cfg = Some(config);
        }
    }

    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_set_event_callback(
    callback: EventCallback,
    user_data: *mut c_void,
) -> i32 {
    let state = match get_state() {
        Some(s) => s,
        None => {
            set_last_error("Engine not initialized");
            return -1;
        }
    };

    let mut cb_guard = state.callback.lock().unwrap();
    *cb_guard = Some((callback, user_data));
    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_start_server(bind_ip: *const c_char, port: u16) -> i32 {
    let state = match get_state() {
        Some(s) => s,
        None => {
            set_last_error("Engine not initialized");
            return -1;
        }
    };

    let ip_str = if bind_ip.is_null() {
        "0.0.0.0".to_string()
    } else {
        CStr::from_ptr(bind_ip).to_str().unwrap_or("0.0.0.0").to_string()
    };

    let bind_addr = format!("{}:{}", ip_str, port);
    state.server_running.store(true, Ordering::SeqCst);
    let server_running = state.server_running.clone();

    state.runtime.spawn(async move {
        if let Ok(listener) = TcpListener::bind(&bind_addr).await {
            if let Ok(local_addr) = listener.local_addr() {
                let payload = serde_json::json!({
                    "ip": local_addr.ip().to_string(),
                    "port": local_addr.port()
                })
                .to_string();
                dispatch_event(DZ_EVENT_SERVER_STARTED, &payload);
            }

            while server_running.load(Ordering::SeqCst) {
                if let Ok((mut socket, _peer_addr)) = listener.accept().await {
                    tokio::spawn(async move {
                        if let Ok((header, payload)) = read_frame(&mut socket).await {
                            if header.frame_type == FrameType::TransferRequest {
                                if let Ok(json) = std::str::from_utf8(&payload) {
                                    dispatch_event(DZ_EVENT_TRANSFER_REQUEST, json);
                                }
                            }
                        }
                    });
                }
            }
        }
    });

    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_stop_server() -> i32 {
    if let Some(state) = get_state() {
        state.server_running.store(false, Ordering::SeqCst);
    }
    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_start_discovery(
    device_name: *const c_char,
    port: u16,
) -> i32 {
    let state = match get_state() {
        Some(s) => s,
        None => {
            set_last_error("Engine not initialized");
            return -1;
        }
    };

    let dev_name = if device_name.is_null() {
        "DropZone Device".to_string()
    } else {
        CStr::from_ptr(device_name)
            .to_str()
            .unwrap_or("DropZone Device")
            .to_string()
    };

    let dev_id = {
        let cfg = state.config.lock().unwrap();
        cfg.as_ref()
            .map(|c| c.device_id.clone())
            .unwrap_or_else(|| "dz-dev-unknown".to_string())
    };

    state
        .discovery
        .start(dev_id, dev_name, port, |discovered_json| {
            dispatch_event(DZ_EVENT_PEER_DISCOVERED, &discovered_json);
        });

    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_stop_discovery() -> i32 {
    if let Some(state) = get_state() {
        state.discovery.stop();
    }
    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_send_job(
    peer_ip: *const c_char,
    peer_port: u16,
    manifest_json: *const c_char,
) -> i32 {
    let state = match get_state() {
        Some(s) => s,
        None => {
            set_last_error("Engine not initialized");
            return -1;
        }
    };

    if peer_ip.is_null() || manifest_json.is_null() {
        set_last_error("Null arguments to send_job");
        return -1;
    }

    let ip = match CStr::from_ptr(peer_ip).to_str() {
        Ok(s) => s.to_string(),
        Err(_) => return -2,
    };

    let manifest_str = match CStr::from_ptr(manifest_json).to_str() {
        Ok(s) => s.to_string(),
        Err(_) => return -3,
    };

    let addr = format!("{}:{}", ip, peer_port);

    state.runtime.spawn(async move {
        match TcpStream::connect(&addr).await {
            Ok(mut stream) => {
                let payload = manifest_str.into_bytes();
                let header = FrameHeader::new(FrameType::TransferRequest, 1, payload.len() as u32);
                if let Err(e) = write_frame(&mut stream, &header, &payload).await {
                    let err_json = serde_json::json!({
                        "job_id": "",
                        "error_code": -10,
                        "message": format!("Failed to send manifest: {}", e)
                    })
                    .to_string();
                    dispatch_event(DZ_EVENT_ERROR, &err_json);
                }
            }
            Err(e) => {
                let err_json = serde_json::json!({
                    "job_id": "",
                    "error_code": -11,
                    "message": format!("Connection failed to {}: {}", addr, e)
                })
                .to_string();
                dispatch_event(DZ_EVENT_ERROR, &err_json);
            }
        }
    });

    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_accept_job(
    job_id: *const c_char,
    resume_offset: u64,
) -> i32 {
    if job_id.is_null() {
        return -1;
    }
    let id_str = match CStr::from_ptr(job_id).to_str() {
        Ok(s) => s.to_string(),
        Err(_) => return -2,
    };

    let payload = serde_json::json!({
        "job_id": id_str,
        "resume_offset": resume_offset
    })
    .to_string();
    dispatch_event(DZ_EVENT_TRANSFER_ACCEPTED, &payload);
    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_reject_job(
    job_id: *const c_char,
    reason: *const c_char,
) -> i32 {
    if job_id.is_null() {
        return -1;
    }
    let id_str = match CStr::from_ptr(job_id).to_str() {
        Ok(s) => s.to_string(),
        Err(_) => return -2,
    };

    let reason_str = if reason.is_null() {
        "Declined"
    } else {
        CStr::from_ptr(reason).to_str().unwrap_or("Declined")
    };

    let payload = serde_json::json!({
        "job_id": id_str,
        "reason": reason_str
    })
    .to_string();
    dispatch_event(DZ_EVENT_TRANSFER_REJECTED, &payload);
    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_pause_job(job_id: *const c_char) -> i32 {
    if job_id.is_null() {
        return -1;
    }
    if let Ok(id_str) = CStr::from_ptr(job_id).to_str() {
        if let Some(state) = get_state() {
            state.sessions.pause_job(id_str);
            let payload = serde_json::json!({ "job_id": id_str }).to_string();
            dispatch_event(DZ_EVENT_PAUSED, &payload);
        }
    }
    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_resume_job(job_id: *const c_char) -> i32 {
    if job_id.is_null() {
        return -1;
    }
    if let Ok(id_str) = CStr::from_ptr(job_id).to_str() {
        if let Some(state) = get_state() {
            state.sessions.resume_job(id_str);
            let payload = serde_json::json!({ "job_id": id_str }).to_string();
            dispatch_event(DZ_EVENT_RESUMED, &payload);
        }
    }
    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_cancel_job(job_id: *const c_char) -> i32 {
    if job_id.is_null() {
        return -1;
    }
    if let Ok(id_str) = CStr::from_ptr(job_id).to_str() {
        if let Some(state) = get_state() {
            state.sessions.cancel_job(id_str);
            let payload =
                serde_json::json!({ "job_id": id_str, "reason": "User cancelled" }).to_string();
            dispatch_event(DZ_EVENT_CANCELLED, &payload);
        }
    }
    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_shutdown() -> i32 {
    if let Some(state) = get_state() {
        state.server_running.store(false, Ordering::SeqCst);
        state.discovery.stop();
    }
    0
}

#[no_mangle]
pub unsafe extern "C" fn dropzone_engine_get_last_error() -> *const c_char {
    LAST_ERROR.with(|cell| cell.borrow().as_ptr())
}
