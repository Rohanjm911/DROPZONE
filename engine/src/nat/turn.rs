use super::candidate::{CandidateType, NatCandidate};
use super::stun::STUN_MAGIC_COOKIE;
use std::net::{IpAddr, Ipv4Addr};

pub const ALLOCATE_REQUEST: u16 = 0x0003;
pub const ALLOCATE_RESPONSE: u16 = 0x0103;
pub const ATTR_XOR_RELAYED_ADDRESS: u16 = 0x0016;
pub const ATTR_DATA: u16 = 0x0013;
pub const ATTR_XOR_PEER_ADDRESS: u16 = 0x0012;

pub struct TurnClient;

impl TurnClient {
    /// Builds an RFC 5766 TURN Allocation Request
    pub fn build_allocate_request(transaction_id: &[u8; 12]) -> Vec<u8> {
        let mut packet = Vec::with_capacity(28);
        packet.extend_from_slice(&ALLOCATE_REQUEST.to_be_bytes());

        // Attributes: REQUESTED-TRANSPORT attribute (0x0019), length 4, UDP = 17 << 24
        let attr_len = 8u16;
        packet.extend_from_slice(&attr_len.to_be_bytes());
        packet.extend_from_slice(&STUN_MAGIC_COOKIE.to_be_bytes());
        packet.extend_from_slice(transaction_id);

        // Attribute 0x0019: REQUESTED-TRANSPORT
        packet.extend_from_slice(&0x0019u16.to_be_bytes());
        packet.extend_from_slice(&4u16.to_be_bytes());
        packet.push(17); // UDP protocol 17
        packet.push(0);
        packet.push(0);
        packet.push(0);

        packet
    }

    /// Formats a TURN ChannelData framing header (RFC 5766 Section 11)
    pub fn format_channel_data(channel_number: u16, payload: &[u8]) -> Vec<u8> {
        let len = payload.len() as u16;
        let mut framed = Vec::with_capacity(4 + payload.len() + 4);
        framed.extend_from_slice(&channel_number.to_be_bytes());
        framed.extend_from_slice(&len.to_be_bytes());
        framed.extend_from_slice(payload);

        // 4-byte padding
        let pad = (4 - (payload.len() % 4)) % 4;
        for _ in 0..pad {
            framed.push(0);
        }

        framed
    }

    /// Parses a TURN ChannelData framing header
    pub fn parse_channel_data(buf: &[u8]) -> Result<(u16, &[u8]), &'static str> {
        if buf.len() < 4 {
            return Err("ChannelData frame too short");
        }

        let channel_number = u16::from_be_bytes([buf[0], buf[1]]);
        let payload_len = u16::from_be_bytes([buf[2], buf[3]]) as usize;

        if buf.len() < 4 + payload_len {
            return Err("ChannelData payload truncated");
        }

        Ok((channel_number, &buf[4..4 + payload_len]))
    }

    /// Parses an Allocate Response to extract the XOR-RELAYED-ADDRESS
    pub fn parse_allocate_response(
        buf: &[u8],
        expected_tx_id: &[u8; 12],
    ) -> Result<NatCandidate, &'static str> {
        if buf.len() < 20 {
            return Err("Header too short");
        }

        let msg_type = u16::from_be_bytes([buf[0], buf[1]]);
        if msg_type != ALLOCATE_RESPONSE {
            return Err("Not a TURN Allocate Response");
        }

        let msg_len = u16::from_be_bytes([buf[2], buf[3]]) as usize;
        if &buf[8..20] != expected_tx_id {
            return Err("Transaction ID mismatch");
        }

        let mut offset = 20;
        let end = 20 + msg_len;

        while offset + 4 <= end {
            let attr_type = u16::from_be_bytes([buf[offset], buf[offset + 1]]);
            let attr_len = u16::from_be_bytes([buf[offset + 2], buf[offset + 3]]) as usize;
            offset += 4;

            if offset + attr_len > end {
                return Err("Attribute overflows message");
            }

            if attr_type == ATTR_XOR_RELAYED_ADDRESS {
                let val = &buf[offset..offset + attr_len];
                let family = val[1];
                let x_port = u16::from_be_bytes([val[2], val[3]]);
                let port = x_port ^ ((STUN_MAGIC_COOKIE >> 16) as u16);

                if family == 0x01 && val.len() >= 8 {
                    let x_ip = u32::from_be_bytes([val[4], val[5], val[6], val[7]]);
                    let ip = Ipv4Addr::from(x_ip ^ STUN_MAGIC_COOKIE);
                    let priority = (0 << 24) | (65535 - port as u32); // Lower priority than direct or srflx
                    return Ok(NatCandidate::new(
                        CandidateType::Relayed,
                        IpAddr::V4(ip),
                        port,
                        priority,
                    ));
                }
            }

            let padded_len = (attr_len + 3) & !3;
            offset += padded_len;
        }

        Err("No XOR-RELAYED-ADDRESS found in TURN allocate response")
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use rand_core::{OsRng, RngCore};

    #[test]
    fn test_turn_channel_data_roundtrip() {
        let channel = 0x4001u16;
        let payload = b"E2EE-ENCRYPTED-FILE-CHUNK-BYTES";

        let framed = TurnClient::format_channel_data(channel, payload);
        let (parsed_chan, parsed_payload) =
            TurnClient::parse_channel_data(&framed).expect("Failed to parse");

        assert_eq!(channel, parsed_chan);
        assert_eq!(payload.as_slice(), parsed_payload);
    }

    #[test]
    fn test_turn_allocate_request_building() {
        let mut tx_id = [0u8; 12];
        OsRng.fill_bytes(&mut tx_id);

        let req = TurnClient::build_allocate_request(&tx_id);
        assert_eq!(&req[0..2], &ALLOCATE_REQUEST.to_be_bytes());
        assert_eq!(&req[8..20], &tx_id);
    }
}

