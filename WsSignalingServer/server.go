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
	log.Printf("=== NEW WEBSOCKET CONNECTION ATTEMPT ===")
	log.Printf("Remote address: %s", r.RemoteAddr)
	log.Printf("User agent: %s", r.UserAgent())
	log.Printf("Headers: %v", r.Header)
	
	conn, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		log.Printf("❌ WebSocket upgrade failed: %v", err)
		return
	}
	defer conn.Close()
	log.Printf("✅ WebSocket connection upgraded successfully")

	// Получаем peer ID из query параметра
	peerID := r.URL.Query().Get("peer_id")
	if peerID == "" {
		log.Printf("❌ No peer_id provided in query parameters")
		log.Printf("Available query params: %v", r.URL.Query())
		return
	}
	log.Printf("📋 Peer ID from query: %s", peerID)

	// Получаем room code из query параметра
	roomCode := r.URL.Query().Get("room")
	if roomCode == "" {
		log.Printf("❌ No room code provided in query parameters")
		log.Printf("Available query params: %v", r.URL.Query())
		return
	}
	log.Printf("🏠 Room code from query: %s", roomCode)

	// Создаем нового пира
	peer := &Peer{
		ID:   peerID,
		Conn: conn,
	}
	log.Printf("👤 Created new peer object for: %s", peerID)

	// Добавляем пира в менеджер и комнату
	log.Printf("➕ Adding peer %s to peer manager", peerID)
	s.peerManager.AddPeer(peer)
	
	log.Printf("➕ Adding peer %s to room %s", peerID, roomCode)
	s.roomManager.AddPeerToRoom(roomCode, peer)

	// Уведомляем других пиров в комнате о новом участнике
	log.Printf("📢 Broadcasting peer_joined message for %s to room %s", peerID, roomCode)
	s.roomManager.BroadcastToRoom(roomCode, SignalMessage{
		Type:    "peer_joined",
		From:    peerID,
		To:      "",
		Payload: json.RawMessage(`{"peer_id": "` + peerID + `"}`),
	}, peerID)

	log.Printf("🔄 Starting message processing loop for peer %s", peerID)
	// Обрабатываем сообщения от пира
	for {
		var msg SignalMessage
		if err := conn.ReadJSON(&msg); err != nil {
			log.Printf("❌ Error reading message from peer %s: %v", peerID, err)
			break
		}

		log.Printf("📨 Received message from %s: type=%s, to=%s, payload=%s", peerID, msg.Type, msg.To, string(msg.Payload))
		
		// Обрабатываем сообщение
		s.handleMessage(peer, msg)
	}

	log.Printf("🔌 Peer %s disconnected, cleaning up", peerID)
	// Удаляем пира при отключении
	s.peerManager.RemovePeer(peerID)
	if peer.RoomCode != "" {
		log.Printf("➖ Removing peer %s from room %s", peerID, peer.RoomCode)
		s.roomManager.RemovePeerFromRoom(peer.RoomCode, peerID)

		// Уведомляем других пиров об уходе участника
		log.Printf("📢 Broadcasting peer_left message for %s to room %s", peerID, peer.RoomCode)
		s.roomManager.BroadcastToRoom(peer.RoomCode, SignalMessage{
			Type:    "peer_left",
			From:    peerID,
			To:      "",
			Payload: json.RawMessage(`{"peer_id": "` + peerID + `"}`),
		}, "")
	}
	log.Printf("✅ Cleanup completed for peer %s", peerID)
}

// handleMessage обрабатывает входящие сообщения
func (s *SignalingServer) handleMessage(peer *Peer, msg SignalMessage) {
	log.Printf("🔍 Processing message from %s: type=%s, to=%s, payload=%s", peer.ID, msg.Type, msg.To, string(msg.Payload))

	switch msg.Type {
	case "offer", "answer", "ice_candidate":
		log.Printf("📤 Processing %s message from %s to %s", msg.Type, peer.ID, msg.To)
		// Пересылаем сообщение конкретному пиру
		if msg.To != "" {
			log.Printf("🎯 Attempting to send %s message to specific peer: %s", msg.Type, msg.To)
			if !s.roomManager.SendToPeer(msg.To, msg) {
				log.Printf("❌ Failed to send message to peer %s", msg.To)
			} else {
				log.Printf("✅ Successfully sent %s message to peer %s", msg.Type, msg.To)
			}
		} else {
			log.Printf("⚠️  Message has empty 'to' field, cannot forward %s message", msg.Type)
		}

	case "join_room":
		log.Printf("🚪 Processing join_room message from %s", peer.ID)
		// Пир присоединяется к комнате
		var payload struct {
			RoomCode string `json:"room_code"`
		}
		if err := json.Unmarshal(msg.Payload, &payload); err == nil {
			log.Printf("➕ Adding peer %s to room %s via join_room message", peer.ID, payload.RoomCode)
			s.roomManager.AddPeerToRoom(payload.RoomCode, peer)
		} else {
			log.Printf("❌ Failed to parse join_room payload: %v", err)
		}

	case "leave_room":
		log.Printf("🚪 Processing leave_room message from %s", peer.ID)
		// Пир покидает комнату
		if peer.RoomCode != "" {
			log.Printf("➖ Removing peer %s from room %s via leave_room message", peer.ID, peer.RoomCode)
			s.roomManager.RemovePeerFromRoom(peer.RoomCode, peer.ID)
		} else {
			log.Printf("⚠️  Peer %s tried to leave room but has no room code", peer.ID)
		}

	default:
		log.Printf("❓ Unknown message type: %s from peer %s", msg.Type, peer.ID)
	}
}

// Start запускает сервер
func (s *SignalingServer) Start(addr string) error {
	http.HandleFunc("/ws", s.handleWebSocket)

	log.Printf("🚀 Starting signaling server on %s", addr)
	log.Printf("📡 WebSocket endpoint: ws://localhost%s/ws", addr)
	log.Printf("📋 Usage: ws://localhost%s/ws?peer_id=YOUR_ID&room=ROOM_CODE", addr)
	log.Printf("🔧 Server ready to accept connections")
	return http.ListenAndServe(addr, nil)
}
