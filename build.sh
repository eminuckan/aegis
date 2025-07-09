#!/bin/bash

# Default values
VERSION="1.0.0"
SKIP_PACK=false
SKIP_PUBLISH=false

# Parse command line arguments
while [[ $# -gt 0 ]]; do
  case $1 in
    -v|--version)
      VERSION="$2"
      shift 2
      ;;
    --skip-pack)
      SKIP_PACK=true
      shift
      ;;
    --skip-publish)
      SKIP_PUBLISH=true
      shift
      ;;
    -h|--help)
      echo "Usage: $0 [OPTIONS]"
      echo "Options:"
      echo "  -v, --version VERSION    Set version (default: 1.0.0)"
      echo "  --skip-pack             Skip NuGet package creation"
      echo "  --skip-publish          Skip self-contained executable creation"
      echo "  -h, --help              Show this help message"
      exit 0
      ;;
    *)
      echo "Unknown option $1"
      exit 1
      ;;
  esac
done

echo "🚀 Building Aegis Permission Scan Tool v$VERSION"
echo "============================================="

# Clean previous builds
echo "🧹 Cleaning previous builds..."
rm -rf bin publish obj

# Update version in csproj
echo "📝 Updating version to $VERSION..."
if [[ "$OSTYPE" == "darwin"* ]]; then
    # macOS
    sed -i '' "s/<Version>.*<\/Version>/<Version>$VERSION<\/Version>/" SyncPermissions.csproj
else
    # Linux
    sed -i "s/<Version>.*<\/Version>/<Version>$VERSION<\/Version>/" SyncPermissions.csproj
fi

# Build and create NuGet package
if [ "$SKIP_PACK" = false ]; then
    echo "📦 Creating NuGet package..."
    dotnet pack --configuration Release
    if [ $? -ne 0 ]; then exit 1; fi
    echo "✅ NuGet package created successfully!"
fi

# Create self-contained executables
if [ "$SKIP_PUBLISH" = false ]; then
    echo "🔧 Creating self-contained executables..."
    
    declare -a platforms=(
        "win-x64:windows:.exe"
        "linux-x64:linux:"
        "osx-x64:macos:"
    )
    
    for platform_info in "${platforms[@]}"; do
        IFS=':' read -r runtime folder extension <<< "$platform_info"
        echo "  Building for $runtime..."
        dotnet publish -c Release -r "$runtime" --self-contained -p:PublishSingleFile=true -o "publish/$folder"
        if [ $? -ne 0 ]; then exit 1; fi
        
        # Rename executable to aegis
        old_name="publish/$folder/SyncPermissions$extension"
        new_name="publish/$folder/aegis$extension"
        if [ -f "$old_name" ]; then
            mv "$old_name" "$new_name"
        fi
    done
    echo "✅ Self-contained executables created successfully!"
fi

echo ""
echo "🎉 Build completed successfully!"
echo ""
echo "📋 Available outputs:"
if [ "$SKIP_PACK" = false ]; then
    echo "  • NuGet Package: bin/Release/Aegis.PermissionScan.$VERSION.nupkg"
    echo "    Install with: dotnet tool install -g --add-source bin/Release Aegis.PermissionScan"
fi
if [ "$SKIP_PUBLISH" = false ]; then
    echo "  • Windows:       publish/windows/aegis.exe"
    echo "  • Linux:         publish/linux/aegis"
    echo "  • macOS:         publish/macos/aegis"
fi 