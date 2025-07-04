package main

import (
	"encoding/json"
	"log"
	"net/http"

	"github.com/gorilla/websocket"
)

var upgrader = websocket.Upgrader{
	CheckOrigin: func(r *http.Request) bool {
		return true // Разрешаем все origin для разработки
	},
}

// SignalingServer представляет WebSocket signaling сервер
type SignalingServer struct {
	peerManager *PeerManager
	roomManager *RoomManager
}

// NewSignalingServer создает новый signaling сервер
func NewSignalingServer() *SignalingServer {
	return &SignalingServer{
		peerManager: NewPeerManager(),
		roomManager: NewRoomManager(),
	}
}

// handleWebSocket обрабатывает WebSocket соединения
func (s *SignalingServer) handleWebSocket(w http.ResponseWriter, r *http.Request) {
	conn, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		log.Printf("WebSocket upgrade failed: %v", err)
		return
	}
	defer conn.Close()

	// Получаем peer ID из query параметра
	peerID := r.URL.Query().Get("peer_id")
	if peerID == "" {
		log.Printf("No peer_id provided")
		return
	}

	// Получаем room code из query параметра
	roomCode := r.URL.Query().Get("room")
	if roomCode == "" {
		log.Printf("No room code provided")
		return
	}

	// Создаем нового пира
	peer := &Peer{
		ID:   peerID,
		Conn: conn,
	}

	// Добавляем пира в менеджер и комнату
	s.peerManager.AddPeer(peer)
	s.roomManager.AddPeerToRoom(roomCode, peer)

	// Уведомляем других пиров в комнате о новом участнике
	s.roomManager.BroadcastToRoom(roomCode, SignalMessage{
		Type:    "peer_joined",
		From:    peerID,
		To:      "",
		Payload: json.RawMessage(`{"peer_id": "` + peerID + `"}`),
	}, peerID)

	// Обрабатываем сообщения от пира
	for {
		var msg SignalMessage
		if err := conn.ReadJSON(&msg); err != nil {
			log.Printf("Error reading message from peer %s: %v", peerID, err)
			break
		}

		// Обрабатываем сообщение
		s.handleMessage(peer, msg)
	}

	// Удаляем пира при отключении
	s.peerManager.RemovePeer(peerID)
	if peer.RoomCode != "" {
		s.roomManager.RemovePeerFromRoom(peer.RoomCode, peerID)

		// Уведомляем других пиров об уходе участника
		s.roomManager.BroadcastToRoom(peer.RoomCode, SignalMessage{
			Type:    "peer_left",
			From:    peerID,
			To:      "",
			Payload: json.RawMessage(`{"peer_id": "` + peerID + `"}`),
		}, "")
	}
}

// handleMessage обрабатывает входящие сообщения
func (s *SignalingServer) handleMessage(peer *Peer, msg SignalMessage) {
	log.Printf("Received message from %s: type=%s, to=%s", peer.ID, msg.Type, msg.To)

	switch msg.Type {
	case "offer", "answer", "ice_candidate":
		// Пересылаем сообщение конкретному пиру
		if msg.To != "" {
			if !s.roomManager.SendToPeer(msg.To, msg) {
				log.Printf("Failed to send message to peer %s", msg.To)
			}
		}

	case "join_room":
		// Пир присоединяется к комнате
		var payload struct {
			RoomCode string `json:"room_code"`
		}
		if err := json.Unmarshal(msg.Payload, &payload); err == nil {
			s.roomManager.AddPeerToRoom(payload.RoomCode, peer)
		}

	case "leave_room":
		// Пир покидает комнату
		if peer.RoomCode != "" {
			s.roomManager.RemovePeerFromRoom(peer.RoomCode, peer.ID)
		}

	default:
		log.Printf("Unknown message type: %s", msg.Type)
	}
}

// Start запускает сервер
func (s *SignalingServer) Start(addr string) error {
	http.HandleFunc("/ws", s.handleWebSocket)

	log.Printf("Starting signaling server on %s", addr)
	return http.ListenAndServe(addr, nil)
}
