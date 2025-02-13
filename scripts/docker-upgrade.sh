#!/bin/bash

# Define some variables
IMAGE_NAME="dgzone/rahasia:main"
CONTAINER_NAME="mjopen-api"

# Print information
echo "Starting to update ${CONTAINER_NAME} container..."

# Check if Docker is installed
if ! command -v docker &> /dev/null
then
    echo "Docker is not installed, please install Docker first."
    exit 1
fi

# Pull the latest image
echo "Pulling the latest image ${IMAGE_NAME}..."
docker pull ${IMAGE_NAME}
if [ $? -ne 0 ]; then
    echo "Failed to pull the image, please check the network connection or the image address."
    exit 1
fi

# Stop and remove the existing container
if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
    echo "Stopping the existing container ${CONTAINER_NAME}..."
    docker stop ${CONTAINER_NAME}
    if [ $? -ne 0 ]; then
        echo "Failed to stop the container, please check manually."
        exit 1
    fi
fi
if [ "$(docker ps -aq -f status=exited -f name=${CONTAINER_NAME})" ]; then
    echo "Removing the existing container ${CONTAINER_NAME}..."
    docker rm ${CONTAINER_NAME}
    if [ $? -ne 0 ]; then
        echo "Failed to remove the container, please check manually."
        exit 1
    fi
fi

# Run the new container
echo "Starting the new container ${CONTAINER_NAME}..."
docker run --name ${CONTAINER_NAME} -d --restart=always \
 -p 8088:8080 --user root \
 -v /root/mjopen-api/logs:/app/logs:rw \
 -v /root/mjopen-api/data:/app/data:rw \
 -v /root/mjopen-api/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen-api/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
 -e TZ=Asia/Jakarta \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 ${IMAGE_NAME}
if [ $? -ne 0 ]; then
    echo "Failed to start the new container, please check manually."
    exit 1
fi

echo "Container ${CONTAINER_NAME} updated and started successfully!"
