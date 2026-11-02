use serde::{Deserialize, Serialize};
use std::net::SocketAddr;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use tokio::net::UdpSocket;
use tokio::time::{interval, Duration};

pub const DISCOVERY_PORT: u16 = 52491;

#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct BeaconPayload {
    pub device_id: String,
    pub name: String,
    pub ip: String,
    pub port: u16,
}

pub struct DiscoveryManager {
    running: Arc<AtomicBool>,
}

impl DiscoveryManager {
    pub fn new() -> Self {
        Self {
            running: Arc::new(AtomicBool::new(false)),
        }
    }

    pub fn start<F>(
        &self,
        device_id: String,
        device_name: String,
        server_port: u16,
        on_discovered: F,
    ) where
        F: Fn(String) + Send + Sync + 'static,
    {
        if self.running.swap(true, Ordering::SeqCst) {
            return; // Already running
        }

        let is_running = self.running.clone();
        let on_discovered = Arc::new(on_discovered);

        // Spawn UDP listener
        let listener_running = is_running.clone();
        let listener_cb = on_discovered.clone();
        let my_id = device_id.clone();
        tokio::spawn(async move {
            let addr = format!("0.0.0.0:{}", DISCOVERY_PORT);
            if let Ok(socket) = UdpSocket::bind(&addr).await {
                let _ = socket.set_broadcast(true);
                let mut buf = [0u8; 1024];

                while listener_running.load(Ordering::SeqCst) {
                    if let Ok((len, peer_addr)) = socket.recv_from(&mut buf).await {
                        if let Ok(text) = std::str::from_utf8(&buf[..len]) {
                            if let Ok(mut beacon) = serde_json::from_str::<BeaconPayload>(text) {
                                if beacon.device_id != my_id {
                                    beacon.ip = peer_addr.ip().to_string();
                                    if let Ok(json) = serde_json::to_string(&beacon) {
                                        listener_cb(json);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        // Spawn UDP broadcaster
        let broadcaster_running = is_running.clone();
        tokio::spawn(async move {
            if let Ok(socket) = UdpSocket::bind("0.0.0.0:0").await {
                let _ = socket.set_broadcast(true);
                let broadcast_addr: SocketAddr =
                    format!("255.255.255.255:{}", DISCOVERY_PORT).parse().unwrap();
                let beacon = BeaconPayload {
                    device_id,
                    name: device_name,
                    ip: "".to_string(),
                    port: server_port,
                };
                let payload = serde_json::to_vec(&beacon).unwrap_or_default();
                let mut ticker = interval(Duration::from_secs(3));

                while broadcaster_running.load(Ordering::SeqCst) {
                    ticker.tick().await;
                    let _ = socket.send_to(&payload, broadcast_addr).await;
                }
            }
        });
    }

    pub fn stop(&self) {
        self.running.store(false, Ordering::SeqCst);
    }
}

