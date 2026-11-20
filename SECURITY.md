# Security policy

Cat Bridge handles sign-in credentials (Cat Face) and exposes a small HTTP API to your own Tailscale devices, so security reports are very welcome.

## Reporting a vulnerability
Please **don't open a public issue**. Use GitHub's [private vulnerability reporting](https://github.com/hamzaibnzakir/cat-bridge/security/advisories/new) instead. Include steps to reproduce and the versions affected. You'll get a reply as soon as possible.

## Design notes
- **Bridge:** listens only on the laptop's Tailscale address (100.64.0.0/10, fd7a:115c:a1e0::/48) and 127.0.0.1. Every API call needs a 192-bit pairing key, compared in constant time.
- **Windows sign-in:** the saved Windows password is encrypted with machine-scope DPAPI in a file only SYSTEM and Administrators can read. It's used only after a face check passes. Windows' own sign-in options are never hidden.
- **Face data:** stored as embeddings only (no photos), encrypted, and kept on the PC.
- **Bark alerts:** optional AES-CBC encryption with your own key, so the relay sees only ciphertext.
