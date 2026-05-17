# Yorii Launcher

A modern Minecraft launcher built with focus on performance, lightweight resource usage, and clean visuals.

# Preview
### Light Mode
<img width="1392" height="831" alt="{E2D0AEEF-4C26-44AE-99B4-F7958EB193AC}" src="https://github.com/user-attachments/assets/a9eb7b3d-2882-4ccd-b254-552d36f0954d" />

### Dark Mode
<img width="1392" height="826" alt="{BC956E87-AE3D-445D-BEF0-FECF4C74A59E}" src="https://github.com/user-attachments/assets/7500f73e-15ab-4af4-a1e3-fe163625eb31" />

## Features
- Ely.by account support with skin support
- Fabric and Vanilla version support
- Modern WinUI 3 interface
- Lightweight and optimized launcher behavior

# Installation

1. Download the certificate and MSIX package matching your processor architecture from the Releases section.
2. Open the certificate file and click **Install Certificate**.
3. Select **Local Machine** and click **Next**.
4. Choose **Place all certificates in the following store** and click **Browse...**
5. Select **Trusted Root Certification Authorities** and finish the installation.
6. Install the MSIX package.

# Notes

- You can sign in using an Ely.by account for skin support and authenticated sessions.
- Leaving the password field empty while using the same username as a previously successfully authenticated Ely.by account will launch a cached session:
  - When connected to the internet, skins will load automatically.
  - When disconnected from the internet, the session runs in offline mode.
- Offline skins require the CustomSkinLoader mod.
- By default, the launcher uses the standard `.minecraft` directory.

