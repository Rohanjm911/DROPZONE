pub const MAGIC: &[u8; 4] = b"DZP1";
pub const PROTOCOL_VERSION: u16 = 1;
pub const HEADER_SIZE: usize = 16;
pub const CHUNK_HEADER_SIZE: usize = 64; // 16 (uuid) + 4 (file_idx) + 8 (offset) + 4 (chunk_size) + 32 (sha256)

#[repr(u16)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FrameType {
    HandshakeInit = 1,
    HandshakeAck = 2,
    PairingRequest = 3,
    PairingResponse = 4,
    PairingConfirm = 5,
    PairingReject = 6,
    CandidateExchange = 8,
    Ping = 10,
    Pong = 11,
    TransferRequest = 16,
    TransferAccept = 17,
    TransferReject = 18,
    ChunkData = 32,
    ChunkAck = 33,
    TransferPause = 48,
    TransferResume = 49,
    TransferCancel = 50,
    TransferComplete = 64,
    ErrorNotification = 255,
}

impl FrameType {
    pub fn from_u16(val: u16) -> Option<Self> {
        match val {
            1 => Some(FrameType::HandshakeInit),
            2 => Some(FrameType::HandshakeAck),
            3 => Some(FrameType::PairingRequest),
            4 => Some(FrameType::PairingResponse),
            5 => Some(FrameType::PairingConfirm),
            6 => Some(FrameType::PairingReject),
            8 => Some(FrameType::CandidateExchange),
            10 => Some(FrameType::Ping),
            11 => Some(FrameType::Pong),
            16 => Some(FrameType::TransferRequest),
            17 => Some(FrameType::TransferAccept),
            18 => Some(FrameType::TransferReject),
            32 => Some(FrameType::ChunkData),
            33 => Some(FrameType::ChunkAck),
            48 => Some(FrameType::TransferPause),
            49 => Some(FrameType::TransferResume),
            50 => Some(FrameType::TransferCancel),
            64 => Some(FrameType::TransferComplete),
            255 => Some(FrameType::ErrorNotification),
            _ => None,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FrameHeader {
    pub version: u16,
    pub frame_type: FrameType,
    pub sequence: u32,
    pub payload_len: u32,
}

impl FrameHeader {
    pub fn new(frame_type: FrameType, sequence: u32, payload_len: u32) -> Self {
        Self {
            version: PROTOCOL_VERSION,
            frame_type,
            sequence,
            payload_len,
        }
    }

    pub fn serialize(&self) -> [u8; HEADER_SIZE] {
        let mut buf = [0u8; HEADER_SIZE];
        buf[0..4].copy_from_slice(MAGIC);
        buf[4..6].copy_from_slice(&self.version.to_be_bytes());
        buf[6..8].copy_from_slice(&(self.frame_type as u16).to_be_bytes());
        buf[8..12].copy_from_slice(&self.sequence.to_be_bytes());
        buf[12..16].copy_from_slice(&self.payload_len.to_be_bytes());
        buf
    }

    pub fn deserialize(buf: &[u8; HEADER_SIZE]) -> Result<Self, &'static str> {
        if &buf[0..4] != MAGIC {
            return Err("Invalid protocol magic");
        }
        let version = u16::from_be_bytes([buf[4], buf[5]]);
        if version != PROTOCOL_VERSION {
            return Err("Unsupported protocol version");
        }
        let frame_type_raw = u16::from_be_bytes([buf[6], buf[7]]);
        let frame_type = FrameType::from_u16(frame_type_raw).ok_or("Unknown frame type")?;
        let sequence = u32::from_be_bytes([buf[8], buf[9], buf[10], buf[11]]);
        let payload_len = u32::from_be_bytes([buf[12], buf[13], buf[14], buf[15]]);

        Ok(Self {
            version,
            frame_type,
            sequence,
            payload_len,
        })
    }
}

#[derive(Debug, Clone)]
pub struct ChunkDescriptor {
    pub job_id: [u8; 16],
    pub file_index: u32,
    pub byte_offset: u64,
    pub chunk_size: u32,
    pub sha256: [u8; 32],
}

impl ChunkDescriptor {
    pub fn serialize(&self) -> [u8; CHUNK_HEADER_SIZE] {
        let mut buf = [0u8; CHUNK_HEADER_SIZE];
        buf[0..16].copy_from_slice(&self.job_id);
        buf[16..20].copy_from_slice(&self.file_index.to_be_bytes());
        buf[20..28].copy_from_slice(&self.byte_offset.to_be_bytes());
        buf[28..32].copy_from_slice(&self.chunk_size.to_be_bytes());
        buf[32..64].copy_from_slice(&self.sha256);
        buf
    }

    pub fn deserialize(buf: &[u8]) -> Result<Self, &'static str> {
        if buf.len() < CHUNK_HEADER_SIZE {
            return Err("Buffer too short for chunk header");
        }
        let mut job_id = [0u8; 16];
        job_id.copy_from_slice(&buf[0..16]);

        let file_index = u32::from_be_bytes(buf[16..20].try_into().unwrap());
        let byte_offset = u64::from_be_bytes(buf[20..28].try_into().unwrap());
        let chunk_size = u32::from_be_bytes(buf[28..32].try_into().unwrap());

        let mut sha256 = [0u8; 32];
        sha256.copy_from_slice(&buf[32..64]);

        Ok(Self {
            job_id,
            file_index,
            byte_offset,
            chunk_size,
            sha256,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_header_serialization() {
        let header = FrameHeader::new(FrameType::TransferRequest, 42, 1024);
        let bytes = header.serialize();
        let parsed = FrameHeader::deserialize(&bytes).expect("Failed to deserialize");
        assert_eq!(header, parsed);
    }

    #[test]
    fn test_chunk_descriptor_serialization() {
        let desc = ChunkDescriptor {
            job_id: [1u8; 16],
            file_index: 3,
            byte_offset: 65536,
            chunk_size: 1024,
            sha256: [7u8; 32],
        };
        let bytes = desc.serialize();
        let parsed = ChunkDescriptor::deserialize(&bytes).expect("Failed chunk deserialize");
        assert_eq!(desc.job_id, parsed.job_id);
        assert_eq!(desc.file_index, parsed.file_index);
        assert_eq!(desc.byte_offset, parsed.byte_offset);
        assert_eq!(desc.chunk_size, parsed.chunk_size);
        assert_eq!(desc.sha256, parsed.sha256);
    }
}
