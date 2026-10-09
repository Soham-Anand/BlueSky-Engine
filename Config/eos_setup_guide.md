# Epic Online Services (EOS) Authentication Setup Guide

This guide explains how to set up Epic Online Services (EOS) for BlueSky Engine, configure your application settings in the Epic Games Developer Portal, and enable real browser-based OAuth login.

---

## Step 1: Create a Developer Account & Product

1. Navigate to the [Epic Games Developer Portal](https://dev.epicgames.com/portal/).
2. Sign in with your Epic Games account.
3. Create or join an **Organization**.
4. Click on **Create Product** and name it (e.g., `BlueSky Game`).

---

## Step 2: Retrieve Sandbox & Deployment IDs

Inside your Product panel:
1. Navigate to **Product Settings** (gear icon next to your product name).
2. Under the **General** tab, you will find:
   - **Product ID**
   - **Organization ID**
3. Navigate to the **Sandboxes** tab. Here you will find your Sandbox ID.
4. Navigate to the **Deployments** tab under your sandbox. Here you will find your Deployment ID.

These match the key fields in `EOS.ini`.

---

## Step 3: Configure client credentials & Redirect URIs

To authenticate users via browser login:
1. In the left navigation, go to **Product Settings** -> **Clients**.
2. Click **Add Client** to create a new client credential set.
3. Name your client (e.g., `BlueSkyClient`) and configure the client policy:
   - Assign/Create a policy with permissions suitable for gameplay (e.g., access to basic profile details, presence, lobbies, matchmaking).
4. Under the client configuration, look for the **Redirect URIs** field.
5. Add the following Redirect URIs:
   ```text
   http://localhost
   http://localhost:8080/
   ```
   > [!TIP]
   > For desktop applications, it is highly recommended to register `http://localhost` (without a port). This allows the EOS SDK to automatically search for a free local port dynamically, avoiding collisions if port 8080 is already occupied by another process. If you configure a fixed port like `http://localhost:8080/`, make sure port 8080 is not currently in use by any other web server or development process on your machine.
6. Save the client configuration to retrieve:
   - **Client ID**
   - **Client Secret**


---

## Step 4: Configure local credentials in `EOS.ini`

Open your project's `Config/EOS.ini` file and fill in the details matching your portal setup:

```ini
[EOS]
ProductId=YOUR_PRODUCT_ID_HERE
SandboxId=YOUR_SANDBOX_ID_HERE
DeploymentId=YOUR_DEPLOYMENT_ID_HERE
ClientId=YOUR_CLIENT_ID_HERE
ClientSecret=YOUR_CLIENT_SECRET_HERE
ArtifactId=
ProductName=BlueSky Engine
ProductVersion=0.1.0
```

> [!TIP]
> Do not commit `EOS.ini` with production client secrets to public repositories. You can also configure these credentials using environment variables:
> - `EOS_PRODUCT_ID`
> - `EOS_SANDBOX_ID`
> - `EOS_DEPLOYMENT_ID`
> - `EOS_CLIENT_ID`
> - `EOS_CLIENT_SECRET`

---

## Step 5: Run Standalone and Log In

1. Open the project inside the **BlueSky Editor**.
2. Run the project in **Standalone** mode.
3. Click the **Process Auth / Login** button in the main lobby.
4. Your default web browser will launch and prompt you to log in with your Epic Games account and grant permissions to your product.
5. Once approved, the local web server will handle the redirect code, complete authentication, and grant entry to the game lobby!
