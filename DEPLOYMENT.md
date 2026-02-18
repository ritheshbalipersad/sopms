# Deploying SOPMSApp to Windows Server

This guide covers publishing and running SOPMSApp on a Windows Server, either behind **IIS** or as a **standalone Kestrel** app.

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
| **SQL Server** | Local or remote instance; databases: `entTTSAP`, `MCRegistrationSA` (for login) |
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
  - `DefaultConnection`, `entTTSAPConnection`: e.g. `Server=YourSqlServer;Database=entTTSAP;...`
  - `LoginConnection`: e.g. `Server=YourSqlServer;Database=MCRegistrationSA;...`
  - Use `Trusted_Connection=True` for Windows auth, or `User Id=...;Password=...` for SQL auth.
  - Add `TrustServerCertificate=True` if the server uses a certificate that’s not trusted by the client.
- **StorageSettings:BasePath** – set to a path the app pool/user can read/write (e.g. `D:\SOPMS_Documents` or `C:\Apps\SOPMSApp\Documents`).
- **AllowedHosts** – set to your domain(s) or leave `*` only if acceptable for your environment.

Example (adjust server names and paths):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=entTTSAP;Trusted_Connection=True;TrustServerCertificate=True;",
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

If users upload very large files, in IIS Manager → **Sites** → **SOPMSApp** → **Configuration Editor** → `system.webServer/serverRuntime` → **requestLimits.maxAllowedContentLength** (e.g. 2147483648 for 2 GB). The app already configures Kestrel limits; this is for IIS.

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
