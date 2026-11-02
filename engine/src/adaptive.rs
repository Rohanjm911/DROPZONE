use serde::{Deserialize, Serialize};

pub const MIN_CHUNK_SIZE: u32 = 64 * 1024; // 64 KB
pub const DEFAULT_CHUNK_SIZE: u32 = 1024 * 1024; // 1 MB
pub const MAX_CHUNK_SIZE: u32 = 8 * 1024 * 1024; // 8 MB

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ConnectionRoute {
    DirectLan,
    ReflexiveP2P,
    TurnRelay,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TransferTelemetry {
    pub job_id: String,
    pub rtt_ms: u32,
    pub speed_bytes_per_sec: f64,
    pub chunk_size: u32,
    pub route: ConnectionRoute,
    pub packet_loss_pct: f32,
}

pub struct AdaptiveChunkOptimizer {
    current_chunk_size: u32,
    smoothed_rtt_ms: f32,
}

impl Default for AdaptiveChunkOptimizer {
    fn default() -> Self {
        Self::new()
    }
}

impl AdaptiveChunkOptimizer {
    pub fn new() -> Self {
        Self {
            current_chunk_size: DEFAULT_CHUNK_SIZE,
            smoothed_rtt_ms: 0.0,
        }
    }

    pub fn current_chunk_size(&self) -> u32 {
        self.current_chunk_size
    }

    pub fn update_rtt(&mut self, sample_rtt_ms: u32) {
        if self.smoothed_rtt_ms <= 0.0 {
            self.smoothed_rtt_ms = sample_rtt_ms as f32;
        } else {
            self.smoothed_rtt_ms = (self.smoothed_rtt_ms * 0.7) + (sample_rtt_ms as f32 * 0.3);
        }
    }

    /// Computes optimal next chunk size based on RTT, measured speed, and retries
    pub fn calculate_optimal_chunk_size(
        &mut self,
        speed_bytes_per_sec: f64,
        retry_count: u32,
    ) -> u32 {
        if retry_count > 2 || self.smoothed_rtt_ms > 150.0 {
            // High latency or packet loss: scale down to reduce retransmission waste
            self.current_chunk_size = (self.current_chunk_size / 2).max(MIN_CHUNK_SIZE);
        } else if speed_bytes_per_sec > 100_000_000.0 && self.smoothed_rtt_ms < 5.0 {
            // High-throughput low-latency LAN (e.g. 1Gbps / 10Gbps): scale up to 4MB
            self.current_chunk_size = 4 * 1024 * 1024;
        } else if speed_bytes_per_sec > 30_000_000.0 && self.smoothed_rtt_ms < 25.0 {
            // Standard gigabit LAN / fast Wi-Fi 6: scale up to 2MB
            self.current_chunk_size = 2 * 1024 * 1024;
        } else if speed_bytes_per_sec < 2_000_000.0 || self.smoothed_rtt_ms > 80.0 {
            // Slower link: 512KB
            self.current_chunk_size = 512 * 1024;
        } else {
            // Standard default: 1MB
            self.current_chunk_size = DEFAULT_CHUNK_SIZE;
        }

        self.current_chunk_size = self.current_chunk_size.clamp(MIN_CHUNK_SIZE, MAX_CHUNK_SIZE);
        self.current_chunk_size
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_adaptive_scaling_up_on_fast_lan() {
        let mut opt = AdaptiveChunkOptimizer::new();
        opt.update_rtt(2); // 2ms LAN RTT

        let chunk = opt.calculate_optimal_chunk_size(120_000_000.0, 0); // 120 MB/s
        assert_eq!(chunk, 4 * 1024 * 1024); // 4MB
    }

    #[test]
    fn test_adaptive_scaling_down_on_lossy_wan() {
        let mut opt = AdaptiveChunkOptimizer::new();
        opt.update_rtt(200); // 200ms WAN RTT

        let chunk = opt.calculate_optimal_chunk_size(5_000_000.0, 3); // 3 retries
        assert!(chunk <= 512 * 1024);
    }
}

