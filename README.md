# Aegis - Permission Scan CLI Tool

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/download)
[![NuGet](https://img.shields.io/badge/NuGet-Aegis.PermissionScan-blue.svg)](https://github.com/eminuckan/aegis)

A powerful CLI tool to automatically discover and manage permissions in .NET microservices projects using the IEndpoint pattern.

## ✨ Features

- 🔍 **Endpoint Discovery**: Automatically finds IEndpoint implementations across your projects
- 📊 **Authorization Analysis**: Detects RequireAuthorization() and RequirePermission() usage patterns  
- ⚙️ **Convention-Based Generation**: Generates permissions following HTTP Method → Action + Feature Folder → Resource conventions
- ✅ **Validation**: Validates existing permissions against conventions and reports mismatches
- 📄 **JSON Output**: Structured output format for integration with other tools
- 🎯 **Smart Detection**: Categorizes endpoints as Public, AuthOnly, NeedsPermission, AlreadyProtected, or MismatchedPermission
- ⚙️ **Configuration Support**: Uses aegis-config.json for default values and convention mappings
- 🤝 **Interactive Mode**: User-friendly menu system when run without arguments
- 🚀 **Cross-Platform**: Available as .NET Global Tool and self-contained executables

## 📦 Installation

### Option 1: .NET Global Tool (Recommended)

```bash
# Install from NuGet (coming soon)
dotnet tool install -g Aegis.PermissionScan

# Or install from local build
git clone https://github.com/eminuckan/aegis.git
cd aegis
dotnet pack --configuration Release
dotnet tool install -g --add-source bin/Release Aegis.PermissionScan

# Use anywhere with:
aegis --help
```

### Option 2: Self-Contained Executables

Download the latest release for your platform:

- **Windows**: [aegis.exe](https://github.com/eminuckan/aegis/releases) (No .NET required)
- **Linux**: [aegis](https://github.com/eminuckan/aegis/releases) (No .NET required)  
- **macOS**: [aegis](https://github.com/eminuckan/aegis/releases) (No .NET required)

### Option 3: Build from Source

```bash
git clone https://github.com/eminuckan/aegis.git
cd aegis
dotnet build --configuration Release
dotnet run -- --help
```

## 🚀 Quick Start

```bash
# Interactive mode - guided setup and scanning
aegis

# Scan specific path
aegis scan --path ./src/Services

# Scan with detailed output and save to JSON
aegis scan --path ./src/Services --verbose --output permissions.json

# Setup configuration wizard
aegis setup

# Validate existing permissions
aegis validate --path ./src/Services
```

## ⚙️ Configuration

Create an `aegis-config.json` file in your project root or working directory:

```json
{
  "SyncPermissions": {
    "DefaultScanPath": "./src/Services",
    "DefaultOutputPath": "permissions.json",
    "Verbose": false,
    "MissingOnly": false,
    "AutoGenerate": false,
    "AcceptAllSuggestedPermissions": false
  },
  "Conventions": {
    "HttpMethodActions": {
      "GET": "Read",
      "POST": "Create",
      "PUT": "Update",
      "DELETE": "Delete",
      "PATCH": "Update"
    },
    "FeatureToResource": {
      "RoleManagement": "Roles",
      "UserManagement": "Users",
      "TenantManagement": "Tenants",
      "PermissionManagement": "Permissions"
    }
  }
}
```

## 📋 Commands

### `aegis scan`

Scans projects to analyze endpoints and discover permissions.

```bash
# Basic usage
aegis scan

# Scan specific path
aegis scan --path ./src/Services

# Verbose output with JSON export
aegis scan --path ./src/Services --verbose --output permissions.json

# Show only missing permissions
aegis scan --missing-only

# Auto-accept suggested permission corrections
aegis scan --accept-all
```

**Options:**
- `-p, --path <PATH>`: Path to scan for projects
- `-o, --output <FILE>`: Output file for JSON results
- `-v, --verbose`: Show detailed output
- `-m, --missing-only`: Only show missing permissions
- `-g, --auto-generate`: Automatically add permissions to code
- `-a, --accept-all`: Accept all suggested permission corrections

### `aegis setup`

Interactive setup wizard to configure defaults and conventions.

```bash
aegis setup
```

### `aegis validate`

Validates existing permissions against conventions.

```bash
aegis validate --path ./src/Services --verbose
```

## 🏗️ Convention Rules

### HTTP Method → Action Mapping
- `GET` → `Read`
- `POST` → `Create`
- `PUT` → `Update`
- `DELETE` → `Delete`
- `PATCH` → `Update`

### Feature Folder → Resource Mapping
The tool automatically detects resources from your project structure:
- `Features/UserManagement/` → `Users`
- `Features/RoleManagement/` → `Roles`
- `Features/TenantManagement/` → `Tenants`

### Generated Permission Format
`{Resource}.{Action}` (e.g., `Users.Create`, `Roles.Update`)

## 📊 Output Examples

### Console Output

```
🔍 Scanning: ./src/Services

🔍 Found 3 projects to scan...
✅ Auth.Api (25 endpoints)
✅ User.Api (18 endpoints)  
✅ Admin.Api (9 endpoints)

=== Scan Summary ===
Total Endpoints: 52
  🌐 Public: 7
  🔒 Auth Only: 0
  ⚠️  Needs Permission: 43
  ✅ Already Protected: 2

Generated Permissions: 43
Warnings: 2

📄 Results saved to permissions.json
```

### JSON Output

```json
{
  "metadata": {
    "toolVersion": "1.0.0",
    "generatedAt": "2024-01-15T10:30:00Z",
    "totalEndpoints": 52,
    "totalProjects": 3
  },
  "summary": {
    "publicEndpoints": 7,
    "authOnlyEndpoints": 0,
    "needsPermissionEndpoints": 43,
    "alreadyProtectedEndpoints": 2,
    "generatedPermissions": 43,
    "warnings": 2
  },
  "projects": [
    {
      "name": "Auth.Api",
      "path": "./src/Services/Auth.Api",
      "endpoints": 25,
      "permissions": [
        {
          "name": "Users.Create",
          "httpMethod": "POST",
          "route": "/api/users",
          "status": "NeedsPermission",
          "sourceFile": "Features/UserManagement/CreateUser.cs"
        }
      ]
    }
  ]
}
```

## 🎯 Endpoint Status Icons

- 🌐 **Public**: No authorization required
- 🔒 **AuthOnly**: Requires authentication only
- ⚠️ **NeedsPermission**: Candidate for permission generation
- ✅ **AlreadyProtected**: Has proper permission check
- ❌ **MismatchedPermission**: Permission doesn't match convention

## 🔧 Development

### Prerequisites
- .NET 9.0 SDK or later
- Git

### Building

```bash
# Clone repository
git clone https://github.com/eminuckan/aegis.git
cd aegis

# Build project
dotnet build

# Run tests (if available)
dotnet test

# Create packages
dotnet pack --configuration Release
```

### Creating Releases

```bash
# Build all platform executables
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/windows
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux
dotnet publish -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true -o publish/macos
```

## 🤝 Contributing

We welcome contributions! Here's how you can help:

1. 🐛 **Bug Reports**: Open an issue with detailed reproduction steps
2. 💡 **Feature Requests**: Suggest new features or improvements
3. 🔧 **Pull Requests**: Submit code improvements or fixes
4. 📖 **Documentation**: Help improve docs and examples

### Development Setup

```bash
git clone https://github.com/eminuckan/aegis.git
cd aegis
dotnet restore
dotnet build
```

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙋‍♂️ Support

- 📫 **Issues**: [GitHub Issues](https://github.com/eminuckan/aegis/issues)
- 💬 **Discussions**: [GitHub Discussions](https://github.com/eminuckan/aegis/discussions)
- 📧 **Email**: [eminuckan@outlook.com](mailto:eminuckan@outlook.com)

## 🌟 Roadmap

- [ ] NuGet package publishing automation
- [ ] CI/CD pipeline with GitHub Actions
- [ ] Auto-code modification feature (write permissions to code)
- [ ] VS Code extension
- [ ] Docker integration
- [ ] More project templates and conventions
- [ ] Semantic versioning automation

---

Made with ❤️ by [Emin Uçkan](https://github.com/eminuckan) 