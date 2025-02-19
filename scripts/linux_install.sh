#!/bin/bash

# =====================================
# MidJourney Proxy Installer
# =====================================

# Define colors
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly NC='\033[0m' # No color

# Global variables
ARCH=""
PKG_MANAGER=""
docker_try=false
docker_installed=false
USE_ACCELERATION=false
CONFIG_FILE="./installer_config"
CONTAINER_NAME="mjopen-api"
IMAGE_NAME="dgzone/rahasia:main"

# ================================
# Utility functions
# ================================

# Startup information
start_info() {
    echo -e "${BLUE}MidJourney Proxy Installation Script${NC}"
    echo -e "${BLUE}Performing pre-launch checks...${NC}"
}

# Print colored message
print_msg() {
    local color="$1"
    local message="$2"
    echo -e "${color}${message}${NC}"
}

# Print error and exit
exit_with_error() {
    local message="$1"
    print_msg "${RED}" "$message"
    exit 1
}

# Detect package manager
detect_package_manager() {
    if command -v dnf &>/dev/null; then
        PKG_MANAGER="dnf"
        print_msg "${GREEN}" "Detected dnf package manager"
    elif command -v yum &>/dev/null; then
        PKG_MANAGER="yum"
        print_msg "${GREEN}" "Detected yum package manager"
    elif command -v apt-get &>/dev/null; then
        PKG_MANAGER="apt-get"
        print_msg "${GREEN}" "Detected apt-get package manager"
    else
        exit_with_error "Unsupported Linux distribution."
    fi
}

# Install necessary packages
install_package() {
    local package="$1"
    print_msg "${YELLOW}" "Installing $package..."
    case "$PKG_MANAGER" in
    apt-get)
        print_msg "${BLUE}" "Updating apt-get..."
        apt-get update -y
        apt-get install "$package" -y || exit_with_error "Installation of $package failed."
        ;;
    yum)
        yum makecache -y
        yum install "$package" -y || exit_with_error "Installation of $package failed."
        ;;
    dnf)
        print_msg "${BLUE}" "Updating dnf..."
        dnf makecache -y
        dnf install "$package" -y || exit_with_error "Installation of $package failed."
        ;;
    *)
        exit_with_error "Unknown package manager."
        ;;
    esac
}

# Check and install dependencies
check_dependencies() {
    print_msg "${BLUE}" "Checking and installing dependencies..."
    for dep in curl jq; do
        if ! command -v "$dep" &>/dev/null; then
            install_package "$dep"
        fi
    done
}

# Check CPU architecture
check_architecture() {
    local arch
    arch=$(uname -m)
    case "$arch" in
    x86_64)
        ARCH="x64"
        print_msg "${GREEN}" "Detected x64 architecture"
        ;;
    aarch64)
        ARCH="arm64"
        print_msg "${GREEN}" "Detected arm64 architecture"
        ;;
    *)
        exit_with_error "Unsupported architecture: $arch"
        ;;
    esac
}

# Load configuration and apply acceleration
load_config() {
    if [ -f "$CONFIG_FILE" ]; then
        source "$CONFIG_FILE"
        print_msg "${GREEN}" "Config file loaded"
    else
        print_msg "${YELLOW}" "No config file found"
        ask_acceleration
    fi
}

# Create running versions configuration file
create_running_versions_file() {
    local file="running_versions.conf"
    # If the file does not exist, create an empty file
    if [ ! -f "$file" ]; then
        touch "$file"
        # print_msg "${GREEN}" "Created running versions configuration file: $file"
    fi
}

check_docker_installed() {
    if ! command -v docker &>/dev/null; then
        docker_installed=false
        print_msg "${YELLOW}" "Docker not installed"
    else
        docker_installed=true
        print_msg "${GREEN}" "Docker is installed"
    fi
}

# Ask if acceleration should be enabled
ask_acceleration() {
    while true; do
        read -rp "Would you like to enable acceleration (fix Docker/GitHub connection issues in China)? [Y/n]: " choice
        choice=$(echo "$choice" | tr '[:upper:]' '[:lower:]')
        if [[ "$choice" == "y" || "$choice" == "" ]]; then
            USE_ACCELERATION=true
            save_config
            break
        elif [[ "$choice" == "n" ]]; then
            USE_ACCELERATION=false
            save_config
            break
        else
            print_msg "${YELLOW}" "Please enter Y or N."
        fi
    done
}

# Save configuration
save_config() {
    echo "USE_ACCELERATION=$USE_ACCELERATION" >"$CONFIG_FILE"
    print_msg "${GREEN}" "Config file saved"
}

# ================================
# Docker operation functions
# ================================

install_docker() {
    if ! command -v docker &>/dev/null; then
        if [ "$docker_try" = true ]; then
            exit_with_error "Docker installation failed, please check error info and retry."
        fi
        print_msg "${YELLOW}" "Using official script to install Docker..."
        if $USE_ACCELERATION; then
            if curl -fsSL https://get.docker.com | bash -s docker --mirror Aliyun; then
                docker_try=true
                install_docker
            else
                exit_with_error "Docker installation failed, please check error info and retry."
            fi
        else
            if curl -fsSL https://get.docker.com | bash -s docker; then
                docker_try=true
                install_docker
            else
                exit_with_error "Docker installation failed, please check error info and retry."
            fi
        fi
    else
        docker_installed=true
        print_msg "${GREEN}" "Docker is installed, no action needed."
    fi
}

run_docker_container() {
    print_msg "${BLUE}" "Pulling the latest Docker image..."
    docker pull ${IMAGE_NAME} || exit_with_error "Failed to pull Docker image."

    # Stop and remove existing container
    if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
        print_msg "${BLUE}" "Stopping existing container ${CONTAINER_NAME}..."
        docker stop ${CONTAINER_NAME}
        if [ $? -ne 0 ]; then
            print_msg "${RED}" "Failed to stop container, please manually check."
            return 1
        fi
    fi

    if [ "$(docker ps -aq -f status=exited -f name=${CONTAINER_NAME})" ]; then
        print_msg "${BLUE}" "Removing existing container ${CONTAINER_NAME}..."
        docker rm ${CONTAINER_NAME}
        if [ $? -ne 0 ]; then
            print_msg "${RED}" "Failed to remove container, please manually check."
            return 1
        fi
    fi

    # Prompt user to input port
    read -rp "Please set external port (press enter for default, default 8088): " external_port
    external_port=${external_port:-8088}  # Use default port 8088

    print_msg "${BLUE}" "Starting Docker container..."
    docker run --name ${CONTAINER_NAME} -d --restart=always \
        -p $external_port:8080 --user root \
        -v /root/mjopen-api/logs:/app/logs:rw \
        -v /root/mjopen-api/data:/app/data:rw \
        -v /root/mjopen-api/attachments:/app/wwwroot/attachments:rw \
        -v /root/mjopen-api/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
        -e TZ=Asia/Shanghai \
        -v /etc/localtime:/etc/localtime:ro \
        -v /etc/timezone:/etc/timezone:ro \
        ${IMAGE_NAME} || exit_with_error "Failed to start Docker container."

    # Get public and private IP addresses
    local public_ip
    local private_ip
    public_ip=$(curl -s ifconfig.me)
    private_ip=$(hostname -I | awk '{print $1}')

    print_msg "${GREEN}" "Container ${CONTAINER_NAME} started successfully, please confirm port settings:"
    print_msg "${GREEN}" "LAN address: http://$private_ip:8080"
    print_msg "${GREEN}" "Public address: http://$public_ip:$external_port"
}

start_docker_container() {
    if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
        print_msg "${YELLOW}" "Container ${CONTAINER_NAME} is already running."
    else
        docker start ${CONTAINER_NAME} && print_msg "${GREEN}" "Container ${CONTAINER_NAME} started." || print_msg "${RED}" "Failed to start container."
    }
}

stop_docker_container() {
    if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
        docker stop ${CONTAINER_NAME} && print_msg "${GREEN}" "Container ${CONTAINER_NAME} stopped." || print_msg "${RED}" "Failed to stop container."
    else
        print_msg "${YELLOW}" "Container ${CONTAINER_NAME} is not running."
    }
}

check_docker_status() {
    if [ "$docker_installed" = false ]; then
        print_msg "${YELLOW}" "Docker not installed."
    else
        if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
            print_msg "${GREEN}" "Container ${CONTAINER_NAME} is running."
        else
            print_msg "${YELLOW}" "Docker is installed, container not started."
        fi
    fi
}

# ================================
# Linux version installation functions
# ================================

# Global variables
LATEST_VERSION=""
DOWNLOAD_URL=""
INSTALLED_VERSIONS=()

# Get the latest version information from GitHub
get_latest_version_info() {
    #local API_URL="https://api.github.com/repos/trueai-org/midjourney-proxy/releases/latest"
    local API_URL="https://api.github.com/repos/dg-devloper/mjopen-api/releases/latest"

    local response

    if $USE_ACCELERATION; then
        API_URL="https://ghproxy.com/$API_URL"
    fi

    response=$(curl -s "$API_URL")
    if [ $? -ne 0 ]; then
        print_msg "${RED}" "Failed to get the latest version info."
        return
    fi

    LATEST_VERSION=$(echo "$response" | jq -r '.tag_name')
    DOWNLOAD_URL=$(echo "$response" | jq -r --arg ARCH "$ARCH" '.assets[] | select(.name | test("midjourney-proxy-linux-\($ARCH)")) | .browser_download_url')

    if [ -z "$LATEST_VERSION" ] || [ -z "$DOWNLOAD_URL" ]; then
        print_msg "${RED}" "Failed to get the latest version info."
        return
    fi
}

# List installed versions
list_installed_versions() {
    INSTALLED_VERSIONS=()
    for version_dir in v*; do
        if [ -d "$version_dir" ]; then
            INSTALLED_VERSIONS+=("$version_dir")
        fi
    done

    if [ "${#INSTALLED_VERSIONS[@]}" -gt 0 ]; then
        echo -e "${BLUE}Installed Linux versions:${NC}"
        for version in "${INSTALLED_VERSIONS[@]}"; do
            echo -e "  $version"
        done
    fi
}

# Install specified version
install_version() {
    local version="$1"

    if [ -d "$version" ]; then
        print_msg "${YELLOW}" "Version $version is already installed."
        return 1
    fi

    local specific_api_url="https://api.github.com/repos/trueai-org/midjourney-proxy/releases/tags/$version"
    # local specific_api_url="https://api.github.com/repos/dg-devloper/mjopen-api/releases/tags/$version"

    if $USE_ACCELERATION; then
        specific_api_url="https://ghproxy.com/$specific_api_url"
    fi
    local response
    response=$(curl -s "$specific_api_url")
    if [ $? -ne 0 ]; then
        print_msg "${RED}" "Failed to get version $version info."
        return
    fi

    local tar_url
    tar_url=$(echo "$response" | jq -r --arg ARCH "$ARCH" --arg VERSION "$version" '
        .assets[]
        | select(.name | test("midjourney-proxy-linux-" + $ARCH + "-" + $VERSION + "\\.tar\\.gz"))
        | .browser_download_url
    ')

    if [ -z "$tar_url" ]; then
        print_msg "${RED}" "Couldn't find the specified version's download link: $version"
        return
    fi

    # Create temporary directory
    local temp_dir
    temp_dir=$(mktemp -d)
    if [ ! -d "$temp_dir" ]; then
        print_msg "${RED}" "Failed to create temporary directory."
        return
    fi
    trap 'rm -rf "$temp_dir"' EXIT

    cd "$temp_dir" || { print_msg "${RED}" "Failed to enter temporary directory."; return; }

    # Download tarball
    download_file "$tar_url" "midjourney-proxy-linux-${ARCH}-${version}.tar.gz" || {
        print_msg "${RED}" "Download failed."
        return
    }

    # Create target directory
    mkdir -p "$OLDPWD/$version"

    # Extract to target directory
    if ! tar -xzf "midjourney-proxy-linux-${ARCH}-${version}.tar.gz" -C "$OLDPWD/$version"; then
        print_msg "${RED}" "Failed to extract file."
        return
    fi

    cd "$OLDPWD" || { print_msg "${RED}" "Failed to return to original directory."; return; }

    print_msg "${GREEN}" "Version $version installed successfully."
}

# Delete specified version
delete_version() {
    read -rp "Please enter the version to delete (e.g., v2.3.7): " version
    if [[ "$version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        if [ -d "$version" ]; then
            read -rp "Are you sure you want to delete $version? [y/N]: " confirm
            confirm=$(echo "$confirm" | tr '[:upper:]' '[:lower:]')
            if [ "$confirm" == "y" ]; then
                rm -rf "$version" && print_msg "${GREEN}" "Version $version deleted." || print_msg "${RED}" "Failed to delete version $version."
            else
                print_msg "${YELLOW}" "Deletion canceled."
            fi
        else
            print_msg "${RED}" "Version $version is not installed."
        fi
    else
        print_msg "${RED}" "Invalid version format."
    fi
}

# Download file with optional acceleration
download_file() {
    local url="$1"
    local output="$2"

    if $USE_ACCELERATION; then
        local proxy_url="https://ghproxy.com/${url#https://}"
        print_msg "${BLUE}" "Using acceleration to download $proxy_url..."
        if ! curl -L -o "$output" "$proxy_url"; then
            print_msg "${RED}" "Download failed, please check network connection."
            return 1
        fi
    else
        print_msg "${BLUE}" "Downloading $url..."
        if ! curl -L -o "$output" "$url"; then
            print_msg "${RED}" "Download failed, please check network connection."
            return 1
        fi
    fi
}

start_version() {
    local version="$1"

    if [ ! -d "$version" ]; then
        print_msg "${RED}" "Version $version is not installed."
        return 1
    fi

    local settings_file=""
    if [ -f "$version/appsettings.Production.json" ]; then
        settings_file="$version/appsettings.Production.json"
    elif [ -f "$version/appsettings.json" ]; then
        cp "$version/appsettings.json" "$version/appsettings.Production.json"
        if [ ! -f "$version/appsettings.Production.json" ]; then
            print_msg "${RED}" "Failed to copy config file."
            return 1
        fi
        settings_file="$version/appsettings.Production.json"
    else
        print_msg "${RED}" "No config file found."
        return 1
    fi

    # Create a temporary file to store uncommented JSON
    local temp_json
    temp_json=$(mktemp)

    # Remove comments (line comments and inline comments)
    sed -E 's,([^:])(//.*),\1,g' "$settings_file" > "$temp_json"

    local urls
    urls=$(jq -r '.urls' "$temp_json")
    rm -f "$temp_json"  # Delete temporary file

    # Get public and private IP addresses
    local public_ip
    local private_ip
    public_ip=$(curl -s ifconfig.me)
    private_ip=$(hostname -I | awk '{print $1}')

    local flag=true

    if [ -z "$urls" ] || [ "$urls" == "null" ]; then
        print_msg "${YELLOW}" "No 'urls' field found in config file."
        flag=false
    fi

    cd "$version" || { print_msg "${RED}" "Cannot enter directory $version"; return 1; }
    chmod +x ./run_app.sh
    nohup ./run_app.sh > "../$version.log" 2>&1 &

    local pid=$!
    cd - > /dev/null || return 1

    # Wait for 3 seconds
    sleep 3

    # Check if the process is still running
    if ps -p "$pid" > /dev/null 2>&1; then
        # Write version number and PID to running versions configuration file
        echo "$version:$pid" >> "running_versions.conf"
        print_msg "${GREEN}" "Version $version started, PID: $pid."

        if $flag; then
            local private_url="${urls//\*/$private_ip}"
            local public_url="${urls//\*/$public_ip}"
            print_msg "${GREEN}" "LAN address: $private_url"
            print_msg "${GREEN}" "Public address: $public_url"
        fi
    else
        print_msg "${RED}" "Version $version failed to start, please check the log file: $version.log"
        return 1
    fi
}

stop_version() {
    local version="$1"

    if [ ! -s "running_versions.conf" ]; then
        print_msg "${YELLOW}" "Version $version is not running."
        return 1
    fi

    local pid
    pid=$(awk -F":" -v ver="$version" '$1 == ver {print $2}' "running_versions.conf")
    if [ -z "$pid" ]; then
        print_msg "${YELLOW}" "Version $version is not running."
        return 1
    fi

    if kill "$pid" > /dev/null 2>&1; then
        print_msg "${GREEN}" "Stopped version $version, PID: $pid"
        grep -vE "^$version:$pid$|^$" "running_versions.conf" > "running_versions.tmp" && mv "running_versions.tmp" "running_versions.conf"
    else
        print_msg "${RED}" "Failed to stop version $version, the process may not exist."
        grep -vE "^$version:$pid$|^$" "running_versions.conf" > "running_versions.tmp" && mv "running_versions.tmp" "running_versions.conf"
    fi
}

list_running_versions() {
    if [ ! -s "running_versions.conf" ]; then
        print_msg "${YELLOW}" "No version is currently running."
        return
    fi

    local running_versions=()
    local updated_entries=()
    while IFS=":" read -r version pid; do
        if [[ -n "$version" && -n "$pid" ]] && ps -p "$pid" > /dev/null 2>&1; then
            running_versions+=("$version (PID: $pid)")
            updated_entries+=("$version:$pid")
        else
            if [[ -n "$version" ]]; then
                print_msg "${YELLOW}" "Stopped, removing record."
            fi
        fi
    done < "running_versions.conf"

    printf "%s\n" "${updated_entries[@]}" > "running_versions.conf"

    if [ "${#running_versions[@]}" -gt 0 ]; then
        print_msg "${GREEN}" "Running versions:"
        for entry in "${running_versions[@]}"; do
            echo "  $entry"
        done
    else
        print_msg "${YELLOW}" "No version is currently running."
    fi
}

update_version() {
    local version="$1"

    if [ ! -d "$version" ]; then
        print_msg "${RED}" "Version $version is not installed, cannot update."
        return 1
    fi

    stop_version "$version"

    print_msg "${BLUE}" "Updating version $version ..."

    # Get the latest version information
    get_latest_version_info
    if [ -z "$LATEST_VERSION" ] || [ -z "$DOWNLOAD_URL" ]; then
        print_msg "${RED}" "Failed to get the latest version info."
        return 1
    fi

    # Check if the version needs to be updated
    if [ "$version" == "$LATEST_VERSION" ]; then
        print_msg "${GREEN}" "Version $version is already the latest, no update needed."
        return 0
    fi

    # Backup configuration file before updating
    if [ ! -f "$version/appsettings.Production.json" ]; then
        if [ -f "$version/appsettings.json" ]; then
            cp "$version/appsettings.json" "$version/appsettings.Production.json"
            print_msg "${GREEN}" "Copied appsettings.json to appsettings.Production.json"
        else
            print_msg "${YELLOW}" "Warning: appsettings.json not found"
        fi
    fi

    # Download the latest version package
    local temp_dir
    temp_dir=$(mktemp -d)
    if [ ! -d "$temp_dir" ]; then
        print_msg "${RED}" "Failed to create temporary directory."
        return 1
    fi
    trap 'rm -rf "$temp_dir"' EXIT

    cd "$temp_dir" || { print_msg "${RED}" "Failed to enter temporary directory."; return 1; }

    # Download the latest version tarball
    print_msg "${BLUE}" "Downloading the latest version package..."
    download_file "$DOWNLOAD_URL" "midjourney-proxy-linux-${ARCH}-${LATEST_VERSION}.tar.gz" || {
        print_msg "${RED}" "Download failed."
        return 1
    }

    # Extract to version directory, overwrite installation
    if ! tar -xzf "midjourney-proxy-linux-${ARCH}-${LATEST_VERSION}.tar.gz" -C "$OLDPWD/$version"; then
        print_msg "${RED}" "Failed to extract file."
        return 1
    fi

    if ! cd "$OLDPWD"; then
        exit_with_error "Failed to go back to original directory."
    fi

    # Rename directory after successful update
    if ! mv "$version" "${LATEST_VERSION}"; then
        print_msg "${RED}" "Failed to rename directory."
        return 1
    fi

    print_msg "${GREEN}" "Version $version updated to the latest version $LATEST_VERSION."
}

# ================================
# Main menu
# ================================

main_menu() {
    while true; do
        echo
        echo -e "${BLUE}Midjourney Proxy Installation Script${NC}"
        check_docker_status
        list_installed_versions
        list_running_versions
        echo -e "1. ${GREEN}Docker version (recommended, x64 only)${NC}"
        echo -e "2. ${GREEN}Linux version (supports x64 and arm64)${NC}"
        echo -e "3. ${GREEN}Exit${NC}"
        read -rp "Please choose (1-3): " choice

        case "$choice" in
        1)
            if [ "$ARCH" != "x64" ]; then
                print_msg "${RED}" "Docker version currently supports x64 architecture only."
            else
                docker_submenu
            fi
            ;;
        2)
            linux_menu
            ;;
        3)
            print_msg "${GREEN}" "Exiting."
            exit 0
            ;;
        *)
            print_msg "${RED}" "Invalid option, please enter a number between 1 and 3."
            ;;
        esac
    done
}

docker_submenu() {
    while true; do
        echo
        check_docker_status
        echo -e "${BLUE}Docker Menu:${NC}"
        echo -e "1. ${GREEN}Install Docker${NC}"
        echo -e "2. ${GREEN}Install or update and start container${NC}"
        echo -e "3. ${GREEN}Start container without updating${NC}"
        echo -e "4. ${GREEN}Stop container${NC}"
        echo -e "5. ${GREEN}Return to main menu${NC}"
        read -rp "Please choose (1-5): " option

        case "$option" in
        1)
            if [ "$docker_installed" = false ]; then
                install_docker
            else
                print_msg "${YELLOW}" "Docker is already installed."
            fi
            ;;
        2)
            if [ "$docker_installed" = true ]; then
                run_docker_container
            else
                print_msg "${RED}" "Docker is not installed, please install Docker first."
            fi
            ;;
        3)
            if [ "$docker_installed" = true ]; then
                start_docker_container
            else
                print_msg "${RED}" "Docker is not installed, please install Docker first."
            fi
            ;;
        4)
            if [ "$docker_installed" = true ]; then
                if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
                    stop_docker_container
                else
                    print_msg "${YELLOW}" "Container $CONTAINER_NAME is not running."
                fi
            else
                print_msg "${RED}" "Docker is not installed, please install Docker first."
            fi
            ;;
        5)
            break
            ;;
        *)
            print_msg "${RED}" "Invalid option, please enter a number between 1 and 5."
            ;;
        esac
    done
}

linux_menu() {
    while true; do
        echo
        list_installed_versions
        list_running_versions
        echo -e "${BLUE}Linux Version Menu:${NC}"
        echo -e "1. ${GREEN}Install latest version${NC}"
        echo -e "2. ${GREEN}Install specified version${NC}"
        echo -e "3. ${GREEN}Delete specified version${NC}"
        echo -e "4. ${GREEN}Start specified version${NC}"
        echo -e "5. ${GREEN}Stop specified version${NC}"
        echo -e "6. ${GREEN}Update installed version${NC}"
        echo -e "7. ${GREEN}Return to main menu${NC}"
        read -rp "Please choose (1-7): " option

        case "$option" in
        1)
            get_latest_version_info
            install_version "$LATEST_VERSION"
            ;;
        2)
            read -rp "Please enter the version to install (e.g., v2.3.7): " version
            install_version "$version"
            ;;
        3)
            delete_version
            ;;
        4)
            read -rp "Please enter the version to start: " version
            start_version "$version"
            ;;
        5)
            read -rp "Please enter the version to stop: " version
            stop_version "$version"
            ;;
        6)
            read -rp "Please enter the version to update: " version
            update_version "$version"
            ;;
        7)
            break
            ;;
        *)
            print_msg "${RED}" "Invalid option, please enter a number between 1 and 7."
            ;;
        esac
    done
}

# ================================
# Script initialization
# ================================

main() {
    detect_package_manager
    check_dependencies
    check_architecture
    create_running_versions_file
    load_config
    check_docker_installed
    main_menu
}

main "$@"