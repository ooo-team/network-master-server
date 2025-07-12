# Makefile for WebSocket Signaling Server

# Variables
IMAGE_NAME = signaling-server
CONTAINER_NAME = signaling-server
TAG = latest
PORT = 8080

# Default target
.PHONY: help
help:
	@echo "Available commands:"
	@echo "  make build     - Build Docker image"
	@echo "  make run       - Run container (detached)"
	@echo "  make stop      - Stop container"
	@echo "  make restart   - Restart container"
	@echo "  make logs      - Show container logs"
	@echo "  make update    - Build and restart (full update)"
	@echo "  make clean     - Remove container and image"
	@echo "  make status    - Show container status"

# Build Docker image
.PHONY: build
build:
	@echo "🔨 Building Docker image..."
	docker build -t $(IMAGE_NAME):$(TAG) .
	@echo "✅ Image built successfully: $(IMAGE_NAME):$(TAG)"

# Run container in detached mode
.PHONY: run
run:
	@echo "🚀 Starting container..."
	docker run -d \
		--name $(CONTAINER_NAME) \
		-p $(PORT):$(PORT) \
		--restart unless-stopped \
		$(IMAGE_NAME):$(TAG)
	@echo "✅ Container started: $(CONTAINER_NAME)"

# Stop container
.PHONY: stop
stop:
	@echo "🛑 Stopping container..."
	-docker stop $(CONTAINER_NAME)
	@echo "✅ Container stopped"

# Remove container
.PHONY: rm
rm:
	@echo "🗑️  Removing container..."
	-docker rm $(CONTAINER_NAME)
	@echo "✅ Container removed"

# Restart container
.PHONY: restart
restart: stop rm run
	@echo "🔄 Container restarted"

# Show container logs
.PHONY: logs
logs:
	@echo "📋 Container logs:"
	docker logs -f $(CONTAINER_NAME)

# Show container status
.PHONY: status
status:
	@echo "📊 Container status:"
	docker ps -a --filter name=$(CONTAINER_NAME)

# Full update: build and restart
.PHONY: update
update:
	@echo "🔄 Full update: building and restarting..."
	$(MAKE) build
	$(MAKE) restart
	@echo "✅ Update completed!"

# Clean everything
.PHONY: clean
clean:
	@echo "🧹 Cleaning up..."
	$(MAKE) stop
	$(MAKE) rm
	-docker rmi $(IMAGE_NAME):$(TAG)
	@echo "✅ Cleanup completed"

# Quick deploy (build + run if not exists, restart if exists)
.PHONY: deploy
deploy:
	@echo "🚀 Deploying signaling server..."
	@if docker ps -a --filter name=$(CONTAINER_NAME) --format "{{.Names}}" | grep -q $(CONTAINER_NAME); then \
		echo "📦 Container exists, updating..."; \
		$(MAKE) update; \
	else \
		echo "🆕 Container doesn't exist, creating..."; \
		$(MAKE) build; \
		$(MAKE) run; \
	fi
	@echo "✅ Deployment completed!"

# Development mode (run with volume mount for live code changes)
.PHONY: dev
dev:
	@echo "🔧 Starting development mode..."
	-docker stop $(CONTAINER_NAME)-dev 2>/dev/null || true
	-docker rm $(CONTAINER_NAME)-dev 2>/dev/null || true
	docker run -d \
		--name $(CONTAINER_NAME)-dev \
		-p $(PORT):$(PORT) \
		-v $(PWD):/app \
		-w /app \
		--restart unless-stopped \
		golang:1.21-alpine sh -c "go mod download && go run ."
	@echo "✅ Development container started: $(CONTAINER_NAME)-dev"

# Show all containers
.PHONY: ps
ps:
	@echo "📋 All containers:"
	docker ps -a

# Show images
.PHONY: images
images:
	@echo "🖼️  Available images:"
	docker images | grep $(IMAGE_NAME) 