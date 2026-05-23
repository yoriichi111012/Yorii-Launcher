# Yorii Launcher

A modern Minecraft launcher built with focus on performance, lightweight resource usage, and clean visuals.

# Preview
### Light Mode
<img width="1404" height="811" alt="{1702E451-7C99-4276-8C75-B554BF4FE3FA}" src="https://github.com/user-attachments/assets/6a08b79c-c641-4f3b-b41e-66d7b55d704c" />

### Dark Mode
<img width="1401" height="811" alt="{42F725FA-A0C2-4A87-BCA9-22338529CC60}" src="https://github.com/user-attachments/assets/022545d7-c782-49cc-8292-5f760adb7897" />

## Features
- Ely.by account support with skin support
- Mod Downloading and Management through launcher
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

