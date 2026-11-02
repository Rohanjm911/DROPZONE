use crate::crypto::{
    compute_x25519_shared_secret, derive_session_keys, generate_x25519_keypair, sha256_hex,
};
use rand_core::{OsRng, RngCore};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use x25519_dalek::StaticSecret;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PairingPayload {
    pub device_id: String,
    pub device_name: String,
    pub public_key_hex: String,
    pub salt_hex: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PairingPinEvent {
    pub peer_device_id: String,
    pub peer_device_name: String,
    pub pin_code: String,
    pub fingerprint: String,
    pub is_initiator: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PairingResultEvent {
    pub peer_device_id: String,
    pub peer_device_name: String,
    pub fingerprint: String,
    pub success: bool,
    pub message: String,
}

pub struct ActivePairingSession {
    pub peer_device_id: String,
    pub peer_device_name: String,
    pub peer_fingerprint: String,
    pub local_secret: StaticSecret,
    pub local_salt: [u8; 32],
    pub derived_pin: String,
    pub is_initiator: bool,
    pub confirmed_by_user: bool,
}

#[derive(Clone, Default)]
pub struct PairingManager {
    sessions: Arc<Mutex<HashMap<String, ActivePairingSession>>>,
}

impl PairingManager {
    pub fn new() -> Self {
        Self {
            sessions: Arc::new(Mutex::new(HashMap::new())),
        }
    }

    /// Prepares an outbound pairing request payload
    pub fn create_pairing_request(
        &self,
        local_device_id: &str,
        local_device_name: &str,
        _peer_device_id: &str,
    ) -> (PairingPayload, StaticSecret, [u8; 32]) {
        let (secret, public) = generate_x25519_keypair();
        let mut salt = [0u8; 32];
        OsRng.fill_bytes(&mut salt);

        let payload = PairingPayload {
            device_id: local_device_id.to_string(),
            device_name: local_device_name.to_string(),
            public_key_hex: hex_encode(public.as_bytes()),
            salt_hex: hex_encode(&salt),
        };

        (payload, secret, salt)
    }

    /// Handles an incoming PairingRequest from a peer
    pub fn handle_incoming_request(
        &self,
        local_device_id: &str,
        local_device_name: &str,
        req: &PairingPayload,
    ) -> Result<(PairingPayload, PairingPinEvent), String> {
        let peer_pub_bytes = hex_decode_32(&req.public_key_hex)?;
        let peer_salt = hex_decode_32(&req.salt_hex)?;

        let (secret, public) = generate_x25519_keypair();
        let mut local_salt = [0u8; 32];
        OsRng.fill_bytes(&mut local_salt);

        // Compute shared secret
        let shared_secret = compute_x25519_shared_secret(&secret, &peer_pub_bytes);

        // Combined salt: local_salt XOR peer_salt
        let mut combined_salt = [0u8; 32];
        for i in 0..32 {
            combined_salt[i] = local_salt[i] ^ peer_salt[i];
        }

        let session_keys = derive_session_keys(&shared_secret, &combined_salt, false);
        let fingerprint = sha256_hex(&peer_pub_bytes);

        let pin_event = PairingPinEvent {
            peer_device_id: req.device_id.clone(),
            peer_device_name: req.device_name.clone(),
            pin_code: session_keys.sas_pin.clone(),
            fingerprint: fingerprint.clone(),
            is_initiator: false,
        };

        let response_payload = PairingPayload {
            device_id: local_device_id.to_string(),
            device_name: local_device_name.to_string(),
            public_key_hex: hex_encode(public.as_bytes()),
            salt_hex: hex_encode(&local_salt),
        };

        // Store active session
        let mut lock = self.sessions.lock().unwrap();
        lock.insert(
            req.device_id.clone(),
            ActivePairingSession {
                peer_device_id: req.device_id.clone(),
                peer_device_name: req.device_name.clone(),
                peer_fingerprint: fingerprint,
                local_secret: secret,
                local_salt,
                derived_pin: session_keys.sas_pin,
                is_initiator: false,
                confirmed_by_user: false,
            },
        );

        Ok((response_payload, pin_event))
    }

    /// Handles incoming PairingResponse for the initiator
    pub fn handle_incoming_response(
        &self,
        resp: &PairingPayload,
        secret: StaticSecret,
        local_salt: [u8; 32],
    ) -> Result<PairingPinEvent, String> {
        let peer_pub_bytes = hex_decode_32(&resp.public_key_hex)?;
        let peer_salt = hex_decode_32(&resp.salt_hex)?;

        let shared_secret = compute_x25519_shared_secret(&secret, &peer_pub_bytes);

        let mut combined_salt = [0u8; 32];
        for i in 0..32 {
            combined_salt[i] = local_salt[i] ^ peer_salt[i];
        }

        let session_keys = derive_session_keys(&shared_secret, &combined_salt, true);
        let fingerprint = sha256_hex(&peer_pub_bytes);

        let pin_event = PairingPinEvent {
            peer_device_id: resp.device_id.clone(),
            peer_device_name: resp.device_name.clone(),
            pin_code: session_keys.sas_pin.clone(),
            fingerprint: fingerprint.clone(),
            is_initiator: true,
        };

        let mut lock = self.sessions.lock().unwrap();
        lock.insert(
            resp.device_id.clone(),
            ActivePairingSession {
                peer_device_id: resp.device_id.clone(),
                peer_device_name: resp.device_name.clone(),
                peer_fingerprint: fingerprint,
                local_secret: secret,
                local_salt,
                derived_pin: session_keys.sas_pin,
                is_initiator: true,
                confirmed_by_user: false,
            },
        );

        Ok(pin_event)
    }

    /// Confirms pairing approval by user
    pub fn confirm_pairing(
        &self,
        peer_device_id: &str,
        expected_pin: &str,
    ) -> Result<PairingResultEvent, String> {
        let mut lock = self.sessions.lock().unwrap();
        let session = lock
            .get_mut(peer_device_id)
            .ok_or_else(|| "No active pairing session found".to_string())?;

        if session.derived_pin != expected_pin {
            return Err("PIN code verification failed".to_string());
        }

        session.confirmed_by_user = true;
        let event = PairingResultEvent {
            peer_device_id: session.peer_device_id.clone(),
            peer_device_name: session.peer_device_name.clone(),
            fingerprint: session.peer_fingerprint.clone(),
            success: true,
            message: "Pairing successfully verified and confirmed".to_string(),
        };

        Ok(event)
    }

    /// Removes an active session
    pub fn cancel_pairing(&self, peer_device_id: &str) {
        let mut lock = self.sessions.lock().unwrap();
        lock.remove(peer_device_id);
    }
}

fn hex_encode(bytes: &[u8]) -> String {
    let mut s = String::with_capacity(bytes.len() * 2);
    for b in bytes {
        use std::fmt::Write;
        let _ = write!(s, "{:02x}", b);
    }
    s
}

fn hex_decode_32(hex: &str) -> Result<[u8; 32], String> {
    if hex.len() != 64 {
        return Err("Hex string must be exactly 64 characters for 32 bytes".to_string());
    }
    let mut out = [0u8; 32];
    for i in 0..32 {
        let byte_str = &hex[i * 2..i * 2 + 2];
        out[i] = u8::from_str_radix(byte_str, 16)
            .map_err(|e| format!("Invalid hex at index {}: {}", i, e))?;
    }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_pairing_manager_flow() {
        let alice = PairingManager::new();
        let bob = PairingManager::new();

        // 1. Alice creates request
        let (req_payload, alice_secret, alice_salt) =
            alice.create_pairing_request("alice-id", "Alice's Laptop", "bob-id");

        // 2. Bob handles request, generates response + pin event
        let (resp_payload, bob_pin_event) = bob
            .handle_incoming_request("bob-id", "Bob's Workstation", &req_payload)
            .expect("Bob failed request");

        // 3. Alice handles response, generates pin event
        let alice_pin_event = alice
            .handle_incoming_response(&resp_payload, alice_secret, alice_salt)
            .expect("Alice failed response");

        // 4. Verify SAS PIN codes match identically
        assert_eq!(alice_pin_event.pin_code, bob_pin_event.pin_code);
        assert_eq!(alice_pin_event.pin_code.len(), 6);

        // 5. User confirms on both sides
        let alice_res = alice
            .confirm_pairing("bob-id", &alice_pin_event.pin_code)
            .expect("Alice confirm failed");
        let bob_res = bob
            .confirm_pairing("alice-id", &bob_pin_event.pin_code)
            .expect("Bob confirm failed");

        assert!(alice_res.success);
        assert!(bob_res.success);
    }
}

