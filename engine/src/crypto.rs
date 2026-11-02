use chacha20poly1305::aead::{Aead, KeyInit};
use chacha20poly1305::{ChaCha20Poly1305, Nonce};
use rand_core::OsRng;
use sha2::{Digest, Sha256};
use x25519_dalek::{PublicKey, StaticSecret};

pub const SHA256_BLOCK_SIZE: usize = 64;
pub const SHA256_OUTPUT_SIZE: usize = 32;

pub fn sha256_digest(data: &[u8]) -> [u8; 32] {
    let mut hasher = Sha256::new();
    hasher.update(data);
    let result = hasher.finalize();
    let mut out = [0u8; 32];
    out.copy_from_slice(&result);
    out
}

pub fn sha256_hex(data: &[u8]) -> String {
    let digest = sha256_digest(data);
    let mut hex = String::with_capacity(64);
    for byte in digest {
        use std::fmt::Write;
        let _ = write!(hex, "{:02x}", byte);
    }
    hex
}

/// RFC 2104 HMAC-SHA256 implementation
pub fn hmac_sha256(key: &[u8], data: &[u8]) -> [u8; 32] {
    let mut k_pad = [0u8; SHA256_BLOCK_SIZE];
    if key.len() > SHA256_BLOCK_SIZE {
        let digest = sha256_digest(key);
        k_pad[..SHA256_OUTPUT_SIZE].copy_from_slice(&digest);
    } else {
        k_pad[..key.len()].copy_from_slice(key);
    }

    let mut k_ipad = [0u8; SHA256_BLOCK_SIZE];
    let mut k_opad = [0u8; SHA256_BLOCK_SIZE];
    for i in 0..SHA256_BLOCK_SIZE {
        k_ipad[i] = k_pad[i] ^ 0x36;
        k_opad[i] = k_pad[i] ^ 0x5c;
    }

    // Inner hash: H(k_ipad || data)
    let mut inner = Sha256::new();
    inner.update(&k_ipad);
    inner.update(data);
    let inner_hash = inner.finalize();

    // Outer hash: H(k_opad || inner_hash)
    let mut outer = Sha256::new();
    outer.update(&k_opad);
    outer.update(&inner_hash);
    let outer_hash = outer.finalize();

    let mut out = [0u8; 32];
    out.copy_from_slice(&outer_hash);
    out
}

/// RFC 5869 HKDF-Extract(salt, IKM)
pub fn hkdf_extract(salt: &[u8], ikm: &[u8]) -> [u8; 32] {
    if salt.is_empty() {
        let default_salt = [0u8; 32];
        hmac_sha256(&default_salt, ikm)
    } else {
        hmac_sha256(salt, ikm)
    }
}

/// RFC 5869 HKDF-Expand(PRK, info, length)
pub fn hkdf_expand(prk: &[u8; 32], info: &[u8], length: usize) -> Vec<u8> {
    assert!(length <= 255 * 32, "HKDF output length too large");
    let mut okm = Vec::with_capacity(length);
    let mut prev_t = Vec::new();
    let num_blocks = (length + 31) / 32;

    for i in 1..=num_blocks {
        let mut block_input = Vec::with_capacity(prev_t.len() + info.len() + 1);
        block_input.extend_from_slice(&prev_t);
        block_input.extend_from_slice(info);
        block_input.push(i as u8);

        let t = hmac_sha256(prk, &block_input);
        prev_t = t.to_vec();
        okm.extend_from_slice(&t);
    }

    okm.truncate(length);
    okm
}

/// X25519 Keypair generation using cryptographically secure OS RNG
pub fn generate_x25519_keypair() -> (StaticSecret, PublicKey) {
    let secret = StaticSecret::random_from_rng(OsRng);
    let public = PublicKey::from(&secret);
    (secret, public)
}

/// Diffie-Hellman shared secret agreement
pub fn compute_x25519_shared_secret(
    secret: &StaticSecret,
    peer_public_bytes: &[u8; 32],
) -> [u8; 32] {
    let peer_public = PublicKey::from(*peer_public_bytes);
    let shared = secret.diffie_hellman(&peer_public);
    *shared.as_bytes()
}

/// Derives a 6-digit numeric Short Authentication String (SAS / PIN code)
pub fn derive_sas_code(prk: &[u8; 32]) -> (u32, String) {
    let okm = hkdf_expand(prk, b"dropzone-v1-sas-code", 4);
    let raw = u32::from_be_bytes([okm[0], okm[1], okm[2], okm[3]]);
    let pin_num = (raw % 900_000) + 100_000; // Guarantees 6 digits (100000 - 999999)
    (pin_num, format!("{:06}", pin_num))
}

#[derive(Debug, Clone)]
pub struct SessionKeys {
    pub send_key: [u8; 32],
    pub recv_key: [u8; 32],
    pub sas_pin: String,
}

/// Derives bidirectional AEAD session keys and SAS PIN from shared secret
pub fn derive_session_keys(
    shared_secret: &[u8; 32],
    salt: &[u8],
    is_initiator: bool,
) -> SessionKeys {
    let prk = hkdf_extract(salt, shared_secret);
    let okm = hkdf_expand(&prk, b"dropzone-v1-aead-session-keys", 64);

    let mut key_a = [0u8; 32];
    let mut key_b = [0u8; 32];
    key_a.copy_from_slice(&okm[0..32]);
    key_b.copy_from_slice(&okm[32..64]);

    let (_, sas_pin) = derive_sas_code(&prk);

    if is_initiator {
        SessionKeys {
            send_key: key_a,
            recv_key: key_b,
            sas_pin,
        }
    } else {
        SessionKeys {
            send_key: key_b,
            recv_key: key_a,
            sas_pin,
        }
    }
}

pub fn encrypt_chunk(
    key: &[u8; 32],
    sequence_nonce: u64,
    plaintext: &[u8],
) -> Result<Vec<u8>, &'static str> {
    let cipher = ChaCha20Poly1305::new(key.into());

    let mut nonce_bytes = [0u8; 12];
    nonce_bytes[4..12].copy_from_slice(&sequence_nonce.to_be_bytes());
    let nonce = Nonce::from_slice(&nonce_bytes);

    cipher
        .encrypt(nonce, plaintext)
        .map_err(|_| "AEAD encryption failed")
}

pub fn decrypt_chunk(
    key: &[u8; 32],
    sequence_nonce: u64,
    ciphertext: &[u8],
) -> Result<Vec<u8>, &'static str> {
    let cipher = ChaCha20Poly1305::new(key.into());

    let mut nonce_bytes = [0u8; 12];
    nonce_bytes[4..12].copy_from_slice(&sequence_nonce.to_be_bytes());
    let nonce = Nonce::from_slice(&nonce_bytes);

    cipher
        .decrypt(nonce, ciphertext)
        .map_err(|_| "AEAD decryption authentication failed")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_sha256() {
        let data = b"hello dropzone";
        let hex = sha256_hex(data);
        assert_eq!(hex.len(), 64);
        assert_eq!(
            hex,
            "905fb293d8c506cd043536f62b5857df620878e80c6ef7f594ac3f2c7ed70b56"
        );
    }

    #[test]
    fn test_hmac_sha256_rfc4231_case2() {
        // RFC 4231 Test Case 2: key = "Jefe", data = "what do ya want for nothing?"
        let key = b"Jefe";
        let data = b"what do ya want for nothing?";
        let hmac = hmac_sha256(key, data);
        let hex: String = hmac.iter().map(|b| format!("{:02x}", b)).collect();
        assert_eq!(
            hex,
            "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843"
        );
    }

    #[test]
    fn test_hkdf_expand_and_extract() {
        let ikm = [0x0bu8; 22];
        let salt = [
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c,
        ];
        let prk = hkdf_extract(&salt, &ikm);
        assert_eq!(prk.len(), 32);

        let okm = hkdf_expand(&prk, b"info test", 42);
        assert_eq!(okm.len(), 42);
    }

    #[test]
    fn test_x25519_key_agreement_and_sas() {
        let (alice_secret, alice_public) = generate_x25519_keypair();
        let (bob_secret, bob_public) = generate_x25519_keypair();

        let _alice_shared = compute_x25519_shared_secret(&alice_secret, alice_public.as_bytes());
        let _bob_shared = compute_x25519_shared_secret(&bob_secret, bob_public.as_bytes());

        // Cross agreement
        let alice_agreed = compute_x25519_shared_secret(&alice_secret, bob_public.as_bytes());
        let bob_agreed = compute_x25519_shared_secret(&bob_secret, alice_public.as_bytes());

        assert_eq!(alice_agreed, bob_agreed);

        let salt = [99u8; 32];
        let alice_keys = derive_session_keys(&alice_agreed, &salt, true);
        let bob_keys = derive_session_keys(&bob_agreed, &salt, false);

        // SAS PIN codes match on both sides
        assert_eq!(alice_keys.sas_pin, bob_keys.sas_pin);
        assert_eq!(alice_keys.sas_pin.len(), 6);

        // Alice's send key == Bob's recv key
        assert_eq!(alice_keys.send_key, bob_keys.recv_key);
        // Bob's send key == Alice's recv key
        assert_eq!(bob_keys.send_key, alice_keys.recv_key);

        // Test encryption with derived keys
        let msg = b"Encrypted with derived Diffie-Hellman session key";
        let cipher = encrypt_chunk(&alice_keys.send_key, 1, msg).unwrap();
        let decrypted = decrypt_chunk(&bob_keys.recv_key, 1, &cipher).unwrap();
        assert_eq!(decrypted, msg);
    }

    #[test]
    fn test_aead_roundtrip() {
        let key = [42u8; 32];
        let seq = 12345u64;
        let plaintext = b"Confidential File Data Chunk Payload";

        let encrypted = encrypt_chunk(&key, seq, plaintext).expect("Encryption failed");
        assert_ne!(encrypted.as_slice(), plaintext);

        let decrypted = decrypt_chunk(&key, seq, &encrypted).expect("Decryption failed");
        assert_eq!(decrypted.as_slice(), plaintext);
    }
}
