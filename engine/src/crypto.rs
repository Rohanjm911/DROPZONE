use chacha20poly1305::aead::{Aead, KeyInit};
use chacha20poly1305::{ChaCha20Poly1305, Nonce};
use sha2::{Digest, Sha256};

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
