use serde::{Deserialize, Serialize};
use std::net::IpAddr;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum CandidateType {
    Host,
    ServerReflexive,
    Relayed,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct NatCandidate {
    pub candidate_type: CandidateType,
    pub ip: String,
    pub port: u16,
    pub priority: u32,
}

impl NatCandidate {
    pub fn new(candidate_type: CandidateType, ip: IpAddr, port: u16, priority: u32) -> Self {
        Self {
            candidate_type,
            ip: ip.to_string(),
            port,
            priority,
        }
    }
}

