use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

pub struct JobControl {
    pub paused: Arc<AtomicBool>,
    pub cancelled: Arc<AtomicBool>,
}

impl JobControl {
    pub fn new() -> Self {
        Self {
            paused: Arc::new(AtomicBool::new(false)),
            cancelled: Arc::new(AtomicBool::new(false)),
        }
    }
}

pub struct SessionManager {
    jobs: Arc<Mutex<HashMap<String, JobControl>>>,
}

impl SessionManager {
    pub fn new() -> Self {
        Self {
            jobs: Arc::new(Mutex::new(HashMap::new())),
        }
    }

    pub fn register_job(&self, job_id: &str) -> JobControl {
        let mut map = self.jobs.lock().unwrap();
        let control = JobControl::new();
        let copy = JobControl {
            paused: control.paused.clone(),
            cancelled: control.cancelled.clone(),
        };
        map.insert(job_id.to_string(), control);
        copy
    }

    pub fn pause_job(&self, job_id: &str) {
        let map = self.jobs.lock().unwrap();
        if let Some(ctrl) = map.get(job_id) {
            ctrl.paused.store(true, Ordering::SeqCst);
        }
    }

    pub fn resume_job(&self, job_id: &str) {
        let map = self.jobs.lock().unwrap();
        if let Some(ctrl) = map.get(job_id) {
            ctrl.paused.store(false, Ordering::SeqCst);
        }
    }

    pub fn cancel_job(&self, job_id: &str) {
        let map = self.jobs.lock().unwrap();
        if let Some(ctrl) = map.get(job_id) {
            ctrl.cancelled.store(true, Ordering::SeqCst);
        }
    }

    pub fn remove_job(&self, job_id: &str) {
        let mut map = self.jobs.lock().unwrap();
        map.remove(job_id);
    }
}

