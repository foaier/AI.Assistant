#!/bin/sh
# 停止并移除本项目容器，保留数据库、上传文件和密钥的数据卷。
set -eu
run_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
docker compose --project-directory "$run_dir" -f "$run_dir/compose.yaml" down
