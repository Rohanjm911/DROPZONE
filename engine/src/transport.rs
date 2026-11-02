use crate::protocol::{FrameHeader, HEADER_SIZE};
use std::io::{Error, ErrorKind};
use tokio::io::{AsyncReadExt, AsyncWriteExt};

pub async fn read_frame<R: AsyncReadExt + Unpin>(
    reader: &mut R,
) -> Result<(FrameHeader, Vec<u8>), Error> {
    let mut header_buf = [0u8; HEADER_SIZE];
    reader.read_exact(&mut header_buf).await?;

    let header = FrameHeader::deserialize(&header_buf)
        .map_err(|e| Error::new(ErrorKind::InvalidData, e))?;

    if header.payload_len > 32 * 1024 * 1024 {
        return Err(Error::new(
            ErrorKind::InvalidData,
            "Frame payload exceeds maximum 32MB limit",
        ));
    }

    let mut payload = vec![0u8; header.payload_len as usize];
    if header.payload_len > 0 {
        reader.read_exact(&mut payload).await?;
    }

    Ok((header, payload))
}

pub async fn write_frame<W: AsyncWriteExt + Unpin>(
    writer: &mut W,
    header: &FrameHeader,
    payload: &[u8],
) -> Result<(), Error> {
    let header_bytes = header.serialize();
    writer.write_all(&header_bytes).await?;
    if !payload.is_empty() {
        writer.write_all(payload).await?;
    }
    writer.flush().await?;
    Ok(())
}

