use super::candidate::{CandidateType, NatCandidate};
use rand_core::{OsRng, RngCore};
use std::net::{IpAddr, Ipv4Addr, Ipv6Addr, SocketAddr};
use std::time::Duration;
use tokio::net::UdpSocket;
use tokio::time::timeout;

pub const STUN_MAGIC_COOKIE: u32 = 0x2112_A442;
pub const BINDING_REQUEST: u16 = 0x0001;
pub const BINDING_RESPONSE: u16 = 0x0101;
pub const ATTR_MAPPED_ADDRESS: u16 = 0x0001;
pub const ATTR_XOR_MAPPED_ADDRESS: u16 = 0x0020;

pub struct StunClient;

impl StunClient {
    /// Formats an RFC 5389 STUN Binding Request packet
    pub fn build_binding_request(transaction_id: &[u8; 12]) -> [u8; 20] {
        let mut packet = [0u8; 20];
        packet[0..2].copy_from_slice(&BINDING_REQUEST.to_be_bytes());
        packet[2..4].copy_from_slice(&0u16.to_be_bytes()); // Length of attributes = 0
        packet[4..8].copy_from_slice(&STUN_MAGIC_COOKIE.to_be_bytes());
        packet[8..20].copy_from_slice(transaction_id);
        packet
    }

    /// Parses an RFC 5389 STUN Binding Response packet
    pub fn parse_binding_response(
        buf: &[u8],
        expected_transaction_id: &[u8; 12],
    ) -> Result<SocketAddr, &'static str> {
        if buf.len() < 20 {
            return Err("Packet too short for STUN header");
        }

        let msg_type = u16::from_be_bytes([buf[0], buf[1]]);
        if msg_type != BINDING_RESPONSE {
            return Err("Not a STUN Binding Response");
        }

        let msg_len = u16::from_be_bytes([buf[2], buf[3]]) as usize;
        let magic_cookie = u32::from_be_bytes([buf[4], buf[5], buf[6], buf[7]]);
        if magic_cookie != STUN_MAGIC_COOKIE {
            return Err("Invalid STUN magic cookie");
        }

        if &buf[8..20] != expected_transaction_id {
            return Err("STUN transaction ID mismatch");
        }

        if buf.len() < 20 + msg_len {
            return Err("Truncated STUN attributes payload");
        }

        // Parse attributes
        let mut offset = 20;
        let end = 20 + msg_len;

        while offset + 4 <= end {
            let attr_type = u16::from_be_bytes([buf[offset], buf[offset + 1]]);
            let attr_len = u16::from_be_bytes([buf[offset + 2], buf[offset + 3]]) as usize;
            offset += 4;

            if offset + attr_len > end {
                return Err("Attribute length overflows STUN message");
            }

            let attr_val = &buf[offset..offset + attr_len];

            if attr_type == ATTR_XOR_MAPPED_ADDRESS {
                return Self::parse_xor_mapped_address(attr_val, expected_transaction_id);
            } else if attr_type == ATTR_MAPPED_ADDRESS {
                return Self::parse_mapped_address(attr_val);
            }

            // Align to 4-byte boundary
            let padded_len = (attr_len + 3) & !3;
            offset += padded_len;
        }

        Err("No XOR-MAPPED-ADDRESS or MAPPED-ADDRESS found in response")
    }

    fn parse_xor_mapped_address(
        val: &[u8],
        transaction_id: &[u8; 12],
    ) -> Result<SocketAddr, &'static str> {
        if val.len() < 8 {
            return Err("XOR-MAPPED-ADDRESS too short");
        }

        let family = val[1];
        let x_port = u16::from_be_bytes([val[2], val[3]]);
        let port = x_port ^ ((STUN_MAGIC_COOKIE >> 16) as u16);

        match family {
            0x01 => {
                // IPv4
                if val.len() < 8 {
                    return Err("IPv4 XOR-MAPPED-ADDRESS too short");
                }
                let x_ip = u32::from_be_bytes([val[4], val[5], val[6], val[7]]);
                let ip_u32 = x_ip ^ STUN_MAGIC_COOKIE;
                let ip = Ipv4Addr::from(ip_u32);
                Ok(SocketAddr::new(IpAddr::V4(ip), port))
            }
            0x02 => {
                // IPv6
                if val.len() < 20 {
                    return Err("IPv6 XOR-MAPPED-ADDRESS too short");
                }
                let mut xor_mask = [0u8; 16];
                xor_mask[0..4].copy_from_slice(&STUN_MAGIC_COOKIE.to_be_bytes());
                xor_mask[4..16].copy_from_slice(transaction_id);

                let mut ip_bytes = [0u8; 16];
                for i in 0..16 {
                    ip_bytes[i] = val[4 + i] ^ xor_mask[i];
                }
                let ip = Ipv6Addr::from(ip_bytes);
                Ok(SocketAddr::new(IpAddr::V6(ip), port))
            }
            _ => Err("Unsupported IP address family in STUN response"),
        }
    }

    fn parse_mapped_address(val: &[u8]) -> Result<SocketAddr, &'static str> {
        if val.len() < 8 {
            return Err("MAPPED-ADDRESS too short");
        }

        let family = val[1];
        let port = u16::from_be_bytes([val[2], val[3]]);

        match family {
            0x01 => {
                let ip_bytes = [val[4], val[5], val[6], val[7]];
                let ip = Ipv4Addr::from(ip_bytes);
                Ok(SocketAddr::new(IpAddr::V4(ip), port))
            }
            0x02 => {
                if val.len() < 20 {
                    return Err("IPv6 MAPPED-ADDRESS too short");
                }
                let mut ip_bytes = [0u8; 16];
                ip_bytes.copy_from_slice(&val[4..20]);
                let ip = Ipv6Addr::from(ip_bytes);
                Ok(SocketAddr::new(IpAddr::V6(ip), port))
            }
            _ => Err("Unsupported IP family in MAPPED-ADDRESS"),
        }
    }

    /// Asynchronously queries a STUN server to discover the external reflexive IP and port
    pub async fn query_server(stun_server: &str) -> Result<NatCandidate, String> {
        let socket = UdpSocket::bind("0.0.0.0:0")
            .await
            .map_err(|e| format!("Failed to bind local UDP socket: {}", e))?;

        let server_addr = tokio::net::lookup_host(stun_server)
            .await
            .map_err(|e| format!("Failed to resolve STUN server {}: {}", stun_server, e))?
            .next()
            .ok_or_else(|| format!("Could not resolve STUN address for {}", stun_server))?;

        let mut tx_id = [0u8; 12];
        OsRng.fill_bytes(&mut tx_id);

        let req = Self::build_binding_request(&tx_id);
        socket
            .send_to(&req, server_addr)
            .await
            .map_err(|e| format!("Failed to send STUN request: {}", e))?;

        let mut buf = [0u8; 1024];
        let res = timeout(Duration::from_millis(2500), socket.recv_from(&mut buf)).await;

        match res {
            Ok(Ok((len, _from))) => {
                let addr = Self::parse_binding_response(&buf[..len], &tx_id)
                    .map_err(|e| format!("STUN parse error: {}", e))?;

                // Priority for reflexive candidates RFC 5245: (type_preference << 24) | ...
                let priority = (100 << 24) | (65535 - addr.port() as u32);
                Ok(NatCandidate::new(
                    CandidateType::ServerReflexive,
                    addr.ip(),
                    addr.port(),
                    priority,
                ))
            }
            Ok(Err(e)) => Err(format!("Socket recv error: {}", e)),
            Err(_) => Err(format!("STUN request to {} timed out", stun_server)),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_stun_binding_request_and_response_parsing() {
        let tx_id = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        let req = StunClient::build_binding_request(&tx_id);

        assert_eq!(&req[0..2], &BINDING_REQUEST.to_be_bytes());
        assert_eq!(&req[4..8], &STUN_MAGIC_COOKIE.to_be_bytes());
        assert_eq!(&req[8..20], &tx_id);

        // Construct mock STUN Binding Response with XOR-MAPPED-ADDRESS
        // Expected external IP: 198.51.100.1, port: 54320
        let target_ip = Ipv4Addr::new(198, 51, 100, 1);
        let target_port = 54320u16;

        let mut resp = Vec::new();
        resp.extend_from_slice(&BINDING_RESPONSE.to_be_bytes());
        resp.extend_from_slice(&12u16.to_be_bytes()); // attr len = 12 (type:2 + len:2 + 8 val)
        resp.extend_from_slice(&STUN_MAGIC_COOKIE.to_be_bytes());
        resp.extend_from_slice(&tx_id);

        // XOR-MAPPED-ADDRESS attribute
        resp.extend_from_slice(&ATTR_XOR_MAPPED_ADDRESS.to_be_bytes());
        resp.extend_from_slice(&8u16.to_be_bytes()); // attribute val length

        // Reserved byte + family (0x01 = IPv4)
        resp.push(0x00);
        resp.push(0x01);

        // X-Port = target_port ^ (MAGIC >> 16)
        let x_port = target_port ^ ((STUN_MAGIC_COOKIE >> 16) as u16);
        resp.extend_from_slice(&x_port.to_be_bytes());

        // X-Address = target_ip ^ MAGIC
        let target_ip_u32 = u32::from(target_ip);
        let x_ip = target_ip_u32 ^ STUN_MAGIC_COOKIE;
        resp.extend_from_slice(&x_ip.to_be_bytes());

        let parsed = StunClient::parse_binding_response(&resp, &tx_id)
            .expect("Failed to parse mock response");

        assert_eq!(parsed.ip(), IpAddr::V4(target_ip));
        assert_eq!(parsed.port(), target_port);
    }
}

