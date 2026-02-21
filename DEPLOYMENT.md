# Deploying SOPMSApp to Windows Server

This guide covers publishing and running SOPMSApp on a Windows Server, either behind **IIS** or as a **standalone Kestrel** app.

---

## Complete Step-by-Step: First-Time IIS Deployment on Windows Server

This section walks through every step to deploy SOPMSApp to IIS on Windows Server, including installing all dependencies from scratch.

### Phase 1: Install Windows Server Roles and IIS

**Step 1.1 – Open Server Manager**

1. Log in to the Windows Server machine as Administrator (or a user with admin rights).
2. Open **Server Manager** (Start → Server Manager).

**Step 1.2 – Add Web Server (IIS) role**

1. In Server Manager, click **Add roles and features**.
2. On **Before You Begin**, click **Next**.
3. **Installation Type**: Select **Role-based or feature-based installation** → **Next**.
4. **Server Selection**: Select your server → **Next**.
5. **Server Roles**: Check **Web Server (IIS)**.
6. If prompted to add features (e.g. .NET Framework, Management Tools), click **Add Features** → **Next**.
7. **Features**: Leave defaults → **Next**.
8. **Web Server Role (IIS)**:
   - **Role Services** – enable:
     - **Web Server** → **Application Development** → **.NET Extensibility 4.8** (if available)
     - **Web Server** → **Application Development** → **ASP.NET 4.8** (if available; needed for some IIS modules)
     - **Web Server** → **Common HTTP Features** → **Default Document**, **Directory Browsing** (optional), **HTTP Errors**, **Static Content**
     - **Web Server** → **Health and Diagnostics** → **HTTP Logging**
     - **Web Server** → **Security** → **Request Filtering**, **Windows Authentication** (if you use Windows auth)
9. Click **Next** → **Confirm** → **Install**.
10. Wait for installation to complete → **Close**.

**Step 1.3 – Install ASP.NET Core Hosting Module (required for IIS)**

1. Download the **.NET 8.0 Hosting Bundle** from:  
   [https://dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)  
   Choose **Hosting Bundle** (includes runtime + ASP.NET Core Module for IIS).
2. Run the installer (`dotnet-hosting-8.0.x-win.exe`).
3. Accept the license → **Install**.
4. Restart IIS after installation:
   ```powershell
   net stop was /y
   net start w3svc
   ```

---

### Phase 2: Install SQL Server and Prepare Databases

**Step 2.1 – SQL Server (if not already installed)**

- If SQL Server is already installed (local or remote), skip to Step 2.2.
- To install SQL Server locally:
  1. Download [SQL Server Express](https://www.microsoft.com/sql-server/sql-server-downloads) or full edition.
  2. Run setup → choose **Basic** or **Custom** installation.
  3. Ensure **Database Engine** is selected.
  4. Use **Mixed Mode (SQL Server and Windows Authentication)** if the app will use SQL login.
  5. Note the instance name (e.g. `localhost`, `.\SQLEXPRESS`, or `SERVERNAME\INSTANCE`).

**Step 2.2 – Create databases**

The app requires these databases:

| Database          | Purpose                               |
|-------------------|----------------------------------------|
| `DocRet` or `entTTSAP` | Main application data (DocRegister, etc.) |
| `entTTSAP`        | Additional entity data                 |
| `MCRegistrationSA`| User login/authentication              |

Using **SQL Server Management Studio** or **sqlcmd**:

```sql
CREATE DATABASE DocRet;
CREATE DATABASE entTTSAP;
CREATE DATABASE MCRegistrationSA;
```

(Or use existing databases if your schema already uses different names; update connection strings accordingly.)

---

### Phase 3: Install Visual C++ Redistributable (for PDF generation)

The app uses **DinkToPdf**, which relies on `libwkhtmltox.dll` (based on wkhtmltopdf). This native DLL may need the **Visual C++ Redistributable**.

**Step 3.1**

1. Download **Visual C++ Redistributable for Visual Studio 2015–2022 (x64)** from:  
   [https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)
2. Run `vc_redist.x64.exe` → Install.
3. Reboot if prompted (or at least restart IIS).

---

### Phase 4: Firewall

Allow HTTP/HTTPS traffic to the web server.

**Option A – Via GUI**

1. **Windows Defender Firewall with Advanced Security** → **Inbound Rules** → **New Rule**.
2. Rule type: **Port** → **TCP** → Specific ports: **80** (and **443** if using HTTPS).
3. **Allow the connection** → Apply to Domain, Private, Public as needed → Name: `SOPMSApp HTTP/HTTPS`.

**Option B – Via PowerShell (run as Administrator)**

```powershell
New-NetFirewallRule -DisplayName "SOPMSApp HTTP" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow
New-NetFirewallRule -DisplayName "SOPMSApp HTTPS" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
```

---

### Phase 5: Create Application Folder and Storage Path

**Step 5.1**

1. Create the application folder, e.g. `C:\Apps\SOPMSApp`.
2. Create the document storage folder, e.g. `D:\SOPMS_Documents` (or `C:\SOPMS_Documents` if no separate drive).

**Step 5.2 – Set permissions**

The IIS application pool identity (e.g. `IIS AppPool\SOPMSApp`) must have:

- **C:\Apps\SOPMSApp**: Read & Execute, List, Read.
- **C:\Apps\SOPMSApp\logs**: Read, Write (create if missing).
- **C:\Apps\SOPMSApp\keys**: Read, Write (create if missing).
- **D:\SOPMS_Documents**: Read, Write, Modify (or Full Control).

Via PowerShell (run as Administrator):

```powershell
$appPath = "C:\Apps\SOPMSApp"
$storagePath = "D:\SOPMS_Documents"
$identity = "IIS AppPool\SOPMSApp"

New-Item -ItemType Directory -Path $appPath -Force
New-Item -ItemType Directory -Path "$appPath\logs" -Force
New-Item -ItemType Directory -Path "$appPath\keys" -Force
New-Item -ItemType Directory -Path $storagePath -Force

$acl = Get-Acl $appPath
$acl.SetAccessRuleProtection($false, $true)
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.AddAccessRule($rule)
Set-Acl $appPath $acl

# Full control for logs, keys, and storage
icacls "$appPath\logs" /grant "${identity}:(OI)(CI)F"
icacls "$appPath\keys" /grant "${identity}:(OI)(CI)F"
icacls $storagePath /grant "${identity}:(OI)(CI)F"
```

---

### Phase 6: Publish and Copy the Application

**Step 6.1 – On your development/build machine**

```powershell
cd c:\Code\SOPMSApp
dotnet publish -c Release -o .\publish
```

**Step 6.2 – Copy to the server**

1. Copy the entire contents of `.\publish` to `C:\Apps\SOPMSApp` on the server.
2. Ensure these folders/files exist after copy:
   - `SOPMSApp.dll`
   - `web.config`
   - `DinkToPdf\64bit\libwkhtmltox.dll`
   - `appsettings.json`

---

### Phase 7: Configure the Application

**Step 7.1 – Edit appsettings.json (or appsettings.Production.json)**

On the server, edit `C:\Apps\SOPMSApp\appsettings.json` (or create `appsettings.Production.json` with production overrides):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=DocRet;Trusted_Connection=True;TrustServerCertificate=True;",
    "entTTSAPConnection": "Server=.;Database=entTTSAP;Trusted_Connection=True;TrustServerCertificate=True;",
    "LoginConnection": "Server=.;Database=MCRegistrationSA;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "StorageSettings": {
    "BasePath": "D:\\SOPMS_Documents"
  },
  "AllowedHosts": "*"
}
```

- Replace `Server=.` with your SQL Server instance (e.g. `Server=SERVERNAME\\SQLEXPRESS`).
- For SQL authentication: `User Id=YourUser;Password=YourPassword;` instead of `Trusted_Connection=True`.
- Ensure `BasePath` exists and the app pool has write access.

**Step 7.2 – Set environment (optional)**

To force Production mode:

1. IIS Manager → **Application Pools** → **SOPMSApp** (you will create this next) → **Advanced Settings**.
2. Under **Process Model** → **Environment Variables** → add:
   - Name: `ASPNETCORE_ENVIRONMENT`  
   - Value: `Production`

---

### Phase 8: Create IIS Application Pool

**Step 8.1**

1. Open **IIS Manager** (Run `inetmgr` or Server Manager → Tools → Internet Information Services (IIS) Manager).
2. In the left pane, select the server name.
3. Double-click **Application Pools**.
4. In the right pane, click **Add Application Pool**.

**Step 8.2 – Configure the pool**

| Setting                    | Value              |
|---------------------------|--------------------|
| Name                      | `SOPMSApp`         |
| .NET CLR version          | **No Managed Code**|
| Managed pipeline mode     | **Integrated**     |
| Start application pool    | Checked            |

Click **OK**.

**Step 8.3 – Advanced settings**

1. Right-click **SOPMSApp** → **Advanced Settings**.
2. **General**:
   - **Enable 32-Bit Applications**: **False** (required for 64-bit `libwkhtmltox.dll`).
3. **Process Model**:
   - **Identity**: `ApplicationPoolIdentity` (default), or a custom domain account if you need specific DB/file permissions.
4. Click **OK**.

---

### Phase 9: Create IIS Website

**Step 9.1 – Add website**

1. In IIS Manager, expand the server node → **Sites**.
2. Right-click **Sites** → **Add Website**.

**Step 9.2 – Configure the site**

| Field           | Value                    |
|-----------------|--------------------------|
| Site name       | `SOPMSApp`               |
| Application pool| `SOPMSApp`               |
| Physical path   | `C:\Apps\SOPMSApp`       |
| Binding type    | `http`                   |
| Port            | `80`                     |
| Host name       | (leave empty for default, or set your domain) |

For HTTPS:

- Type: `https`
- Port: `443`
- SSL certificate: Select your certificate.

Click **OK**.

**Step 9.3 – Large file uploads (optional)**

If users upload files larger than ~30 MB:

1. **Sites** → **SOPMSApp** → **Configuration Editor**.
2. Section: `system.webServer` → `security` → `requestFiltering` → `requestLimits`.
3. Set `maxAllowedContentLength` to `2147483648` (2 GB).
4. Click **Apply**.

---

### Phase 10: Run Database Migrations and Start the Site

**Step 10.1 – First run (applies migrations)**

1. Ensure the app pool is started: **Application Pools** → **SOPMSApp** → **Start**.
2. Browse to the site, e.g. `http://localhost` or `http://yourserver`.
3. The app will apply Entity Framework migrations on first startup.
4. If errors occur, check:
   - `C:\Apps\SOPMSApp\logs\stdout_*.log`
   - Windows Event Viewer → Application

**Step 10.2 – web.config for self-contained publish**

If you published **self-contained** (`--self-contained true`):

1. Edit `C:\Apps\SOPMSApp\web.config`.
2. Change:
   - `processPath="dotnet"` → `processPath=".\SOPMSApp.exe"`
   - `arguments=".\SOPMSApp.dll"` → `arguments=""`
3. Save and recycle the app pool.

---

### Phase 11: Post-Deploy Verification

| Check | Action |
|-------|--------|
| Site loads | Browse to `http://yourserver` |
| Login works | Sign in with a test user (MCRegistrationSA must be configured) |
| File upload | Upload a document and confirm it appears under `StorageSettings:BasePath` |
| PDF generation | Use a feature that generates PDFs (e.g. export) – verifies DinkToPdf / libwkhtmltox |

---

### Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| 500.19 or "Cannot read configuration file" | web.config error or path issue | Check `web.config` syntax; ensure path is correct |
| 500.30 / Process failure | App crash on startup | Check `logs\stdout_*.log`; verify connection strings; ensure `DinkToPdf\64bit\libwkhtmltox.dll` exists |
| 503 Service Unavailable | App pool stopped or misconfigured | Start the app pool; ensure **No Managed Code** and **32-Bit = False** |
| PDF export fails | Missing VC++ Redist or libwkhtmltox | Install Visual C++ Redistributable x64; confirm `libwkhtmltox.dll` in `DinkToPdf\64bit\` |
| SQL login fails | Connection string or permissions | Verify connection strings; ensure SQL user/Windows identity has access to DBs |
| File upload fails | Permissions on BasePath | Grant Read/Write to `IIS AppPool\SOPMSApp` on `StorageSettings:BasePath` |

---

## Updating an existing deployment

Use this workflow when the app is **already installed** and you only need to deploy code/config changes.

### 1. Publish the new version (on your dev/build machine)

```powershell
cd c:\Code\SOPMSApp
dotnet publish -c Release -o .\publish
```

Or with an existing profile (e.g. File System to a server share):

```powershell
dotnet publish -c Release /p:PublishProfile=LiveSystem
```

### 2. Copy files to the server

- **Option A – Full replace (simplest):** Copy the entire contents of `.\publish` over the existing app folder on the server (e.g. `C:\Apps\SOPMSApp`).  
  **Do not overwrite** (back them up or leave in place):
  - **appsettings.json** and **appsettings.Production.json** (server-specific connection strings, BasePath, etc.)
  - **logs** folder (optional; can overwrite if you don’t need old logs)
  - **keys** folder (Data Protection keys; if overwritten, existing cookies/sessions may break)

- **Option B – Replace only app binaries:** Copy everything from `publish` **except** the files/folders above. Then overwrite all `.dll`, `.exe`, `.pdb`, and `web.config` from the new publish.

### 3. Apply database migrations (if you added new EF migrations)

If your changes include new **ApplicationDbContext** migrations:

**On the server** (with the app stopped or using a separate tool):

```powershell
cd C:\Apps\SOPMSApp
dotnet SOPMSApp.dll --no-launch-profile
```

The app runs migrations on startup, so a single startup applies them. Stop the app again if you only wanted to run migrations, or leave it running.

Alternatively, from your dev machine (pointing at the **server’s** database):

```powershell
cd c:\Code\SOPMSApp
dotnet ef database update --context ApplicationDbContext --connection "Server=YourServer;Database=DocRet;..."
```

### 4. Restart the application

- **IIS:** In IIS Manager → **Application Pools** → select the app pool (e.g. **SOPMSApp**) → **Recycle** (or **Stop** then **Start**).
- **Kestrel (standalone):** Stop the running process (e.g. stop the Windows Service or close the console), then start it again from the same folder.

### 5. Quick checklist for updates

| Step | Action |
|------|--------|
| 1 | `dotnet publish -c Release -o .\publish` (or use your publish profile) |
| 2 | Copy publish output to server; preserve server’s **appsettings**, **keys**, and optionally **logs** |
| 3 | If you added migrations, run the app once or `dotnet ef database update` against the server DB |
| 4 | Recycle the IIS app pool or restart the Kestrel process |

---

## 1. Server prerequisites

On the Windows Server machine, install:

| Requirement | Details |
|-------------|---------|
| **.NET 8.0 Runtime (ASP.NET Core)** | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) – install "Hosting Bundle" if using IIS |
| **SQL Server** | Local or remote instance; databases: `DocRet` (main app), `entTTSAP` (entities), `MCRegistrationSA` (login) |
| **IIS** (optional) | Only if you want to host behind IIS – enable "ASP.NET Core Module V2" |

- For **IIS**: Install [.NET 8.0 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0) (includes runtime + ASP.NET Core Module). Then ensure the **ASP.NET Core** feature is installed in IIS.
- For **standalone (Kestrel only)**: Install [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (no IIS required).

---

## 2. Publish the application

From your dev machine (or build server), in the solution directory:

### Option A: Publish to a folder (recommended for copying to server)

```powershell
cd c:\Code\SOPMSApp

# Framework-dependent (requires .NET 8 on server) – smaller size
dotnet publish -c Release -o .\publish

# Or self-contained (includes .NET runtime – no install on server) for Windows x64
dotnet publish -c Release -r win-x64 --self-contained true -o .\publish
```

Output will be in `.\publish`. Copy the entire `publish` folder to the server (e.g. `C:\Apps\SOPMSApp`).

### Option B: Use existing File System profile (e.g. LiveSystem)

In Visual Studio: **Right-click project → Publish → LiveSystem** (or the profile that points to your server share/folder).

Or via CLI:

```powershell
dotnet publish -c Release /p:PublishProfile=LiveSystem
```

### Option C: Publish via IIS / Web Deploy (if configured)

Use the **IISProfile** (or your Web Deploy profile) from Visual Studio: **Right-click project → Publish → IISProfile**.

---

## 3. Configuration on the server

### 3.1 Update `appsettings.json` (or use `appsettings.Production.json`)

Edit the published app’s config (e.g. `C:\Apps\SOPMSApp\appsettings.Production.json` or override `appsettings.json`) on the server:

- **Connection strings** – point to the server’s SQL instance and databases:
  - `DefaultConnection`: e.g. `Server=YourSqlServer;Database=DocRet;...` (main app data, DocRegisters)
  - `entTTSAPConnection`: e.g. `Server=YourSqlServer;Database=entTTSAP;...` (entities, departments, etc.)
  - `LoginConnection`: e.g. `Server=YourSqlServer;Database=MCRegistrationSA;...`
  - Use `Trusted_Connection=True` for Windows auth, or `User Id=...;Password=...` for SQL auth.
  - Add `TrustServerCertificate=True` if the server uses a certificate that’s not trusted by the client.
- **StorageSettings:BasePath** – set to a path the app pool/user can read/write (e.g. `D:\SOPMS_Documents` or `C:\Apps\SOPMSApp\Documents`).
- **AllowedHosts** – set to your domain(s) or leave `*` only if acceptable for your environment.

Example (adjust server names and paths):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=DocRet;Trusted_Connection=True;TrustServerCertificate=True;",
    "entTTSAPConnection": "Server=.;Database=entTTSAP;Trusted_Connection=True;TrustServerCertificate=True;",
    "LoginConnection": "Server=.;Database=MCRegistrationSA;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "StorageSettings": {
    "BasePath": "D:\\SOPMS_Documents"
  },
  "AllowedHosts": "yourserver.domain.com;localhost"
}
```

### 3.2 Run database migrations on the server database

On a machine that can reach the SQL Server used in production (or on the server itself), run once:

```powershell
cd C:\Apps\SOPMSApp
dotnet SOPMSApp.dll --no-launch-profile
# Or run migrations explicitly (if you have dotnet-ef on server):
# dotnet ef database update --context ApplicationDbContext
```

The app’s startup code runs `Migrate()` for `ApplicationDbContext`, so the database will be updated on first run if the app has the correct connection string. Alternatively, run `dotnet ef database update --context ApplicationDbContext` from your build machine pointing at the production connection string.

### 3.3 PDF library (DinkToPdf)

The app copies `DinkToPdf\64bit\libwkhtmltox.dll` into the output. For a **64-bit** app pool or process, ensure the published folder contains:

- `DinkToPdf\64bit\libwkhtmltox.dll`

If you publish as **win-x64** self-contained, the same DLL is used. No extra install is needed.

### 3.4 Permissions

- **IIS**: Application pool identity (e.g. `IIS AppPool\SOPMSApp`) must have:
  - Read/execute on the app folder (e.g. `C:\Apps\SOPMSApp`).
  - Read/write on `StorageSettings:BasePath` and on the `logs` and `keys` folders under the app (if used).
- **SQL Server**: The app pool (or the user running the exe) must have access to the SQL instance and the databases above.
- **Data Protection**: The app writes keys under `Application:DataProtection:KeyStoragePath` (default `./keys`). That folder must be writable by the process.

---

## 4. Hosting option 1: IIS

### 4.1 Install / check IIS and ASP.NET Core Module

- Install [.NET 8.0 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0).
- In IIS Manager, confirm that the site or app pool can load the ASP.NET Core Module (no need to create a site yet).

### 4.2 Create application folder and copy publish output

- Create a folder for the app, e.g. `C:\Apps\SOPMSApp`.
- Copy the full contents of your `publish` output into that folder.

### 4.3 Create Application Pool

1. IIS Manager → **Application Pools** → **Add Application Pool**.
2. Name: e.g. **SOPMSApp**.
3. .NET CLR version: **No Managed Code**.
4. Managed pipeline mode: **Integrated**.
5. Start application pool immediately: checked.
6. **Advanced settings** → set **Identity** if needed (e.g. a dedicated domain account with DB and file access).
7. **Advanced settings** → **Enable 32-Bit Applications**: **False** (for 64-bit `libwkhtmltox.dll`).

### 4.4 Create website (or application under Default Web Site)

**New site:**

1. **Sites** → **Add Website**.
2. Site name: **SOPMSApp**.
3. Application pool: **SOPMSApp**.
4. Physical path: **C:\Apps\SOPMSApp**.
5. Binding: e.g. **http**, port **80** (or **https**, **443** with a certificate).

**Or add as application under Default Web Site:**

1. **Default Web Site** → **Add Application**.
2. Alias: **sopms** (or another path).
3. Application pool: **SOPMSApp**.
4. Physical path: **C:\Apps\SOPMSApp**.

### 4.5 web.config (already in publish output)

The published folder should already contain a `web.config` like:

```xml
<aspNetCore processPath="dotnet" arguments=".\SOPMSApp.dll" stdoutLogEnabled="true" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
```

- For **framework-dependent** publish: `processPath="dotnet"` and `arguments=".\SOPMSApp.dll"` are correct.
- For **self-contained** publish: set `processPath=".\SOPMSApp.exe"` and remove `arguments`, or use `arguments=""`.
- Set `stdoutLogEnabled="true"` temporarily if you need to troubleshoot startup errors (logs under `.\logs\stdout`).

### 4.6 Large file uploads (optional)

If users upload very large files, in IIS Manager → **Sites** → **SOPMSApp** → **Configuration Editor** → `system.webServer/security/requestFiltering/requestLimits` → **maxAllowedContentLength** (e.g. 2147483648 for 2 GB). The app already configures Kestrel limits; this is the IIS request filter limit.

---

## 5. Hosting option 2: Standalone (Kestrel) – no IIS

### 5.1 Publish self-contained (optional but simple on server)

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o .\publish
```

Copy `publish` to the server, e.g. `C:\Apps\SOPMSApp`.

### 5.2 Run directly

```powershell
cd C:\Apps\SOPMSApp
.\SOPMSApp.exe
```

The app listens on the URLs configured in `applicationUrl` (e.g. in `Properties\launchSettings.json` for dev). In production, set URLs via:

- **Environment variables**: `ASPNETCORE_URLS=http://0.0.0.0:5000` (or your port).
- Or in **appsettings.Production.json**: the app uses Kestrel endpoints if configured.

To listen on a specific port:

```powershell
$env:ASPNETCORE_URLS = "http://0.0.0.0:5000"
.\SOPMSApp.exe
```

### 5.3 Run as a Windows Service (optional)

So the app runs after reboot and without a logged-in user:

1. Use **sc.exe** or **New-Service** to create a service that runs `C:\Apps\SOPMSApp\SOPMSApp.exe` (self-contained) or `dotnet C:\Apps\SOPMSApp\SOPMSApp.dll` (framework-dependent).
2. Set the working directory to `C:\Apps\SOPMSApp`.
3. Set `ASPNETCORE_URLS` in the service’s environment or in config.
4. Alternatively, use a generic “run exe as service” wrapper (e.g. NSSM, or a small worker that hosts the web app) if you prefer.

---

## 6. Post-deploy checklist

- [ ] Connection strings point to the correct SQL Server and databases.
- [ ] `StorageSettings:BasePath` exists and the process identity has read/write access.
- [ ] Migrations have been applied (app does it on startup, or run `dotnet ef database update` once).
- [ ] `DinkToPdf\64bit\libwkhtmltox.dll` is present in the app folder (64-bit app pool or process).
- [ ] Firewall allows HTTP/HTTPS to the app (IIS or Kestrel port).
- [ ] If using HTTPS in IIS, a certificate is bound and optional redirect from HTTP to HTTPS is configured.
- [ ] Test login (or test user) works; if using MCRegistrationSA, ensure the login DB and stored procedure are available.

---

## 7. Quick reference – publish and copy

```powershell
# On your dev machine
cd c:\Code\SOPMSApp
dotnet publish -c Release -o .\publish

# Copy .\publish\* to server (e.g. C:\Apps\SOPMSApp), then on server:
# - Edit appsettings.Production.json (or appsettings.json) with server DB and paths.
# - For IIS: create app pool + site/app pointing to the folder.
# - For Kestrel: run .\SOPMSApp.exe (or dotnet SOPMSApp.dll) with ASPNETCORE_URLS set.
```

For more detail on IIS and Kestrel, see [Microsoft: Host and deploy ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/).
