package main

import (
	"log"
	"os"
)

func main() {
	// Создаем signaling сервер
	server := NewSignalingServer()

	// Получаем адрес из переменной окружения или используем по умолчанию
	addr := os.Getenv("SIGNALING_ADDR")
	if addr == "" {
		addr = ":8080"
	}

	log.Printf("Starting WebSocket signaling server on %s", addr)
	log.Printf("WebSocket endpoint: ws://localhost%s/ws", addr)
	log.Printf("Usage: ws://localhost%s/ws?peer_id=YOUR_ID&room=ROOM_CODE", addr)

	// Запускаем сервер
	if err := server.Start(addr); err != nil {
		log.Fatal("Server failed to start:", err)
	}
}
