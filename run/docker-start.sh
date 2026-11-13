#!/bin/sh
# 构建并启动完整应用，等待前后端所在容器的健康检查通过。
# 无论从哪个目录执行，都使用本脚本旁的 Compose 配置。
set -eu
run_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
docker compose --project-directory "$run_dir" -f "$run_dir/compose.yaml" up -d --build --wait --wait-timeout 120
printf '%s\n' 'AI.Assistant 已启动，默认地址：http://127.0.0.1:5088' '自定义端口请查看 run/.env，停止服务请执行 sh run/docker-stop.sh。'
