#!/bin/bash

# 定义一些变量
REPO_URL="https://github.com/dg-devloper/mjopen-api.git"
IMAGE_NAME="mjopen-api"   # Nama image lokal yang akan dibuat
CONTAINER_NAME="mjopen-api"
WORK_DIR="/root/mjopen-api"   # Direktori tempat clone repo

# 打印信息
echo "开始更新 ${CONTAINER_NAME} 容器..."

# 验证Docker是否安装
if ! command -v docker &> /dev/null
then
    echo "Docker 未安装，请先安装 Docker。"
    exit 1
fi

# 验证Git是否安装
if ! command -v git &> /dev/null
then
    echo "Git 未安装，请先安装 Git。"
    exit 1
fi

# 克隆最新的GitHub仓库
if [ ! -d "${WORK_DIR}" ]; then
    echo "克隆 GitHub 仓库 ${REPO_URL} ..."
    git clone ${REPO_URL} ${WORK_DIR}
    if [ $? -ne 0 ]; then
        echo "克隆仓库失败，请检查网络连接或仓库地址。"
        exit 1
    fi
else
    echo "仓库已经存在，拉取最新的更新..."
    cd ${WORK_DIR}
    git pull origin main   # Atau branch lain jika diperlukan
    if [ $? -ne 0 ]; then
        echo "拉取最新更新失败，请检查网络连接。"
        exit 1
    fi
fi

# Build Docker image dari source code yang telah di-clone
echo "构建 Docker 镜像 ${IMAGE_NAME}..."
docker build -t ${IMAGE_NAME}:latest ${WORK_DIR}
if [ $? -ne 0 ]; then
    echo "构建 Docker 镜像失败，请手动检查。"
    exit 1
fi

# 拉取最新镜像
# Tidak diperlukan lagi, karena kita sudah membangun image lokal

# 停止并移除现有容器
if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
    echo "停止现有的容器 ${CONTAINER_NAME}..."
    docker stop ${CONTAINER_NAME}
    if [ $? -ne 0 ]; then
        echo "停止容器失败，请手动检查。"
        exit 1
    fi
fi

if [ "$(docker ps -aq -f status=exited -f name=${CONTAINER_NAME})" ]; then
    echo "移除现有的容器 ${CONTAINER_NAME}..."
    docker rm ${CONTAINER_NAME}
    if [ $? -ne 0 ]; then
        echo "移除容器失败，请手动检查。"
        exit 1
    fi
fi

# 运行新的容器
echo "启动新的容器 ${CONTAINER_NAME}..."
docker run --name ${CONTAINER_NAME} -d --restart=always \
 -p 8088:8080 --user root \
 -v /root/mjopen-api/logs:/app/logs:rw \
 -v /root/mjopen-api/data:/app/data:rw \
 -v /root/mjopen-api/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen-api/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 ${IMAGE_NAME}:latest
if [ $? -ne 0 ]; then
    echo "启动新的容器失败，请手动检查。"
    exit 1
fi

echo "容器 ${CONTAINER_NAME} 更新并启动成功！"
