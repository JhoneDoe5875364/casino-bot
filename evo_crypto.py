"""
evo_crypto.py — Evolution live-casino app-layer WebSocket crypto (encrypt + decrypt).

Reconstructed from the deobfuscated client EncryptionService (class D in
static.egcdn.com/frontend/evo/r2/js/commons.7e38152e.js). Verified against captured
HAR traffic: re-encrypting captured plaintext reproduces the captured ciphertext
byte-for-byte (RC4 path), and encrypt()->decrypt() round-trips for arbitrary input.

Frame layout
------------
    byte0 = version (== 1)
    byte1 = flag:
              1 = RC4,     no compression sub-frame
              3 = RC4,     compression sub-frame present   (this is what real traffic uses)
              2 = AES-GCM, no compression sub-frame
              4 = AES-GCM, compression sub-frame present
  RC4 branch:      [ver, flag]        ++ RC4(payload)
  AES-GCM branch:  [ver, flag, 0, 0]  ++ IV(12) ++ AES-GCM-256(payload)   (4-byte header)

Compression sub-frame (only when flag in (3, 4)):
    payload = [subflag] ++ (zstd(data) if subflag == 1 else data)
      subflag 0 -> body stored raw (client falls back to this when the zstd WASM
                   codec is unavailable, or the message is < 256 bytes)
      subflag 1 -> body is a zstd frame (magic 28 b5 2f fd)

Key material (all recoverable from the WS URL query string + the client bundle)
-------------------------------------------------------------------------------
    RC4 key  = utf8( RC4_SALT + ":" + instance_id + ":" + nonce )
    AES key  = PBKDF2(HMAC-SHA256,
                      password = utf8( AES_SALT + ":" + instance_id + ":" + nonce ),
                      salt     = b"evo-crypto-server-v1",
                      iterations = 1, dklen = 32)               -> AES-GCM-256
  instance_id = WebSocket URL  ?instance=...
  nonce       = WebSocket URL  ?nonce=...      (16 random bytes, base64; NOT secret)

The two *_SALT values are 256-bit constants hard-coded in the client JS (identical
for every player), so possession of a HAR capture is sufficient to derive the key
and decrypt — and, with this module, to encrypt frames the client will accept.

CLI
---
    python evo_crypto.py selftest <file.har>          # HAR round-trip / byte-exact test
    python evo_crypto.py keys <file.har>              # print instance_id + nonce per socket
    python evo_crypto.py encrypt <inst> <nonce> '<json>'
    python evo_crypto.py decrypt <inst> <nonce> <hex|base64>
"""

from __future__ import annotations
import json

RC4_SALT = "560a7047c677ab04d413e1f19a19fb517a9e53eeea5ae2f265e8d432131b4192"
AES_SALT = "587907923dc9c616cb82e57085d0184e2b46526e5d7cf2a1bdff0440b747171b"
VERSION = 1
AES_PBKDF2_SALT = b"evo-crypto-server-v1"
ZSTD_MAGIC = b"\x28\xb5\x2f\xfd"


class EvoCrypto:
    def __init__(self, instance_id: str, nonce: str, algorithm: str = "rc4"):
        self.instance_id = instance_id
        self.nonce = nonce
        self.algorithm = algorithm
        self.rc4_key = f"{RC4_SALT}:{instance_id}:{nonce}".encode()
        self._rc4_state = self._rc4_init(self.rc4_key)
        self.aes_derivation_input = f"{AES_SALT}:{instance_id}:{nonce}".encode()
        self._aes_key = None

    # ---------- RC4 (mirrors rc4Init / rc4Process) ----------
    @staticmethod
    def _rc4_init(key: bytes) -> list:
        S = list(range(256))
        j = 0
        for i in range(256):
            j = (j + S[i] + key[i % len(key)]) & 255
            S[i], S[j] = S[j], S[i]
        return S

    def _rc4_process(self, data: bytes) -> bytes:
        # rc4Process copies the initialised state (`this.rc4StateArray.slice()`),
        # so the keystream RESTARTS at position 0 for every message -> keystream
        # reuse within a connection (the two-time-pad weakness).
        S = self._rc4_state[:]
        i = j = 0
        out = bytearray(len(data))
        for n, b in enumerate(data):
            i = (i + 1) & 255
            j = (j + S[i]) & 255
            S[i], S[j] = S[j], S[i]
            out[n] = b ^ S[(S[i] + S[j]) & 255]
        return bytes(out)

    # ---------- optional zstd compression layer ----------
    @staticmethod
    def _compress(data: bytes) -> bytes:
        try:
            import zstandard
            if len(data) >= 256:
                return b"\x01" + zstandard.ZstdCompressor(level=3).compress(data)
        except Exception:
            pass
        return b"\x00" + data  # subflag 0 = stored raw

    @staticmethod
    def _decompress(payload: bytes) -> bytes:
        subflag, body = payload[0], payload[1:]
        if subflag == 0:
            return body
        import zstandard
        return zstandard.ZstdDecompressor().decompress(body)

    # ---------- AES-GCM key (PBKDF2, iterations = 1) ----------
    def _aes_gcm_key(self) -> bytes:
        if self._aes_key is None:
            import hashlib
            self._aes_key = hashlib.pbkdf2_hmac(
                "sha256", self.aes_derivation_input, AES_PBKDF2_SALT, 1, 32)
        return self._aes_key

    # ---------- ENCRYPT ----------
    def encrypt(self, message, compress: bool = True) -> bytes:
        """message: str | bytes | JSON-serialisable object -> frame bytes."""
        if isinstance(message, (bytes, bytearray)):
            raw = bytes(message)
        elif isinstance(message, str):
            raw = message.encode()
        else:
            raw = json.dumps(message, separators=(",", ":"), ensure_ascii=False).encode()

        if self.algorithm == "rc4":
            if compress:
                payload, flag = self._compress(raw), 3
            else:
                payload, flag = raw, 1
            return bytes([VERSION, flag]) + self._rc4_process(payload)

        # AES-GCM
        import os
        from cryptography.hazmat.primitives.ciphers.aead import AESGCM
        payload, flag = (self._compress(raw), 4) if compress else (raw, 2)
        iv = os.urandom(12)
        ct = AESGCM(self._aes_gcm_key()).encrypt(iv, payload, None)
        return bytes([VERSION, flag, 0, 0]) + iv + ct

    # ---------- DECRYPT (the analysed logic) ----------
    def decrypt(self, frame: bytes) -> str:
        return self.decrypt_bytes(frame).decode("utf-8", "replace")

    def decrypt_bytes(self, frame: bytes) -> bytes:
        if len(frame) < 2:
            raise ValueError("frame too short")
        ver, flag = frame[0], frame[1]
        if ver != VERSION:
            raise ValueError(f"unsupported encryption version: {ver}")
        if flag in (1, 3):
            algo = "rc4"
        elif flag in (2, 4):
            algo = "aes-gcm"
        else:
            raise ValueError(f"unsupported encryption algorithm flag: {flag}")
        compressed = flag in (3, 4)

        if algo == "rc4":
            data = self._rc4_process(frame[2:])
        else:
            if len(frame) < 4 + 12 + 16:
                raise ValueError("AES-GCM frame too short")
            from cryptography.hazmat.primitives.ciphers.aead import AESGCM
            iv, ct = frame[4:16], frame[16:]
            data = AESGCM(self._aes_gcm_key()).decrypt(iv, ct, None)

        if compressed:
            data = self._decompress(data)
        return data


# ------------------------------------------------------------------ helpers ----
def key_material_from_url(url: str):
    """Extract (instance_id, nonce) from a game-socket WebSocket URL."""
    import urllib.parse
    qs = urllib.parse.parse_qs(urllib.parse.urlparse(url).query)
    inst = qs.get("instance", [None])[0]
    nonce = qs.get("nonce", [None])[0]
    return inst, nonce


def iter_encrypted_sockets(har_path: str):
    """Yield (url, instance_id, nonce, messages) for each app-encrypted game socket."""
    with open(har_path, encoding="utf-8") as f:
        entries = json.load(f)["log"]["entries"]
    for e in entries:
        url = e["request"]["url"]
        if "nonce=" not in url or "encrypted=true" not in url:
            continue
        msgs = e.get("_webSocketMessages")
        if not msgs:
            continue
        inst, nonce = key_material_from_url(url)
        if inst and nonce:
            yield url, inst, nonce, msgs


# --------------------------------------------------------------------- CLI ----
def _cli(argv):
    import base64
    if len(argv) < 2:
        print(__doc__)
        return 1
    cmd = argv[1]

    if cmd == "keys":
        for url, inst, nonce, msgs in iter_encrypted_sockets(argv[2]):
            print(f"instance={inst}\n  nonce={nonce}\n  frames={len(msgs)}  {url[:80]}...\n")
        return 0

    if cmd == "selftest":
        total_exact = total_zstd = total_rt = total_bad = 0
        for url, inst, nonce, msgs in iter_encrypted_sockets(argv[2]):
            ec = EvoCrypto(inst, nonce)
            exact = zstd = bad = 0
            for m in msgs:
                orig = base64.b64decode(m["data"])
                raw = ec._rc4_process(orig[2:])          # RC4 layer only
                if raw and raw[0] == 1:                   # zstd sub-frame
                    zstd += 1
                    continue
                pt = raw[1:]                              # subflag 0 -> raw payload
                reenc = bytes([1, 3]) + ec._rc4_process(b"\x00" + pt)
                if reenc == orig:
                    exact += 1
                else:
                    bad += 1
            total_exact += exact; total_zstd += zstd; total_bad += bad
            print(f"socket {inst[:24]}...: byte-exact={exact}  zstd-skipped={zstd}  mismatch={bad}")
        # round-trip on synthetic plaintext (uses last socket's key if any)
        ec = EvoCrypto("selftest-instance", "AAAAAAAAAAAAAAAAAAAAAA==")
        for t in ['{"type":"metrics.ping","args":{"t":1}}',
                  '한글 {"amount":123456,"bet":"Player"}']:
            if ec.decrypt(ec.encrypt(t, compress=False)) == t and \
               ec.decrypt(ec.encrypt(t, compress=True)) == t:
                total_rt += 1
            else:
                total_bad += 1
        print(f"\nTOTAL byte-exact={total_exact}  zstd-skipped={total_zstd}  "
              f"roundtrip={total_rt}/2  mismatch={total_bad}")
        print("OK" if total_bad == 0 else "FAILURES PRESENT")
        return 0 if total_bad == 0 else 2

    if cmd == "encrypt":
        _, _, inst, nonce, msg = argv[:5]
        ec = EvoCrypto(inst, nonce)
        fr = ec.encrypt(msg)
        print("hex   :", fr.hex())
        print("base64:", base64.b64encode(fr).decode())
        return 0

    if cmd == "decrypt":
        _, _, inst, nonce, blob = argv[:5]
        try:
            frame = bytes.fromhex(blob)
        except ValueError:
            frame = base64.b64decode(blob)
        print(EvoCrypto(inst, nonce).decrypt(frame))
        return 0

    print(__doc__)
    return 1


if __name__ == "__main__":
    import sys
    raise SystemExit(_cli(sys.argv))
