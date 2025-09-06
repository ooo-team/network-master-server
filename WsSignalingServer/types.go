package main

import (
	"sync"

	"github.com/gorilla/websocket"
)

// Peer представляет подключенного клиента
type Peer struct {
	ID       string
	Conn     *websocket.Conn
	RoomCode string
}

// SignalMessage представляет сообщение для сигналинга
type SignalMessage struct {
	Type    string `json:"type"`
	From    string `json:"from"`
	To      string `json:"to"`
	Payload string `json:"payload"`
}

// Room представляет комнату с подключенными пирами
type Room struct {
	Code  string
	Peers map[string]*Peer
	Mutex sync.RWMutex
}

// Server представляет основной сервер
type Server struct {
	Rooms map[string]*Room
	Mutex sync.Mutex
}
