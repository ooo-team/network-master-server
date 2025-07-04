package main

import (
	"log"
	"sync"
)

// RoomManager управляет комнатами
type RoomManager struct {
	rooms map[string]*Room
	mutex sync.RWMutex
}

// NewRoomManager создает новый менеджер комнат
func NewRoomManager() *RoomManager {
	return &RoomManager{
		rooms: make(map[string]*Room),
	}
}

// CreateRoom создает новую комнату
func (rm *RoomManager) CreateRoom(roomCode string) *Room {
	rm.mutex.Lock()
	defer rm.mutex.Unlock()

	room := &Room{
		Code:  roomCode,
		Peers: make(map[string]*Peer),
	}
	rm.rooms[roomCode] = room
	log.Printf("Room %s created", roomCode)
	return room
}

// GetRoom возвращает комнату по коду
func (rm *RoomManager) GetRoom(roomCode string) *Room {
	rm.mutex.RLock()
	defer rm.mutex.RUnlock()
	return rm.rooms[roomCode]
}

// AddPeerToRoom добавляет пира в комнату
func (rm *RoomManager) AddPeerToRoom(roomCode string, peer *Peer) bool {
	room := rm.GetRoom(roomCode)
	if room == nil {
		room = rm.CreateRoom(roomCode)
	}

	room.Mutex.Lock()
	defer room.Mutex.Unlock()

	peer.RoomCode = roomCode
	room.Peers[peer.ID] = peer
	log.Printf("Peer %s joined room %s", peer.ID, roomCode)
	return true
}

// RemovePeerFromRoom удаляет пира из комнаты
func (rm *RoomManager) RemovePeerFromRoom(roomCode string, peerID string) {
	room := rm.GetRoom(roomCode)
	if room == nil {
		return
	}

	room.Mutex.Lock()
	defer room.Mutex.Unlock()

	delete(room.Peers, peerID)
	log.Printf("Peer %s left room %s", peerID, roomCode)

	// Если комната пустая, удаляем её
	if len(room.Peers) == 0 {
		rm.mutex.Lock()
		delete(rm.rooms, roomCode)
		rm.mutex.Unlock()
		log.Printf("Room %s deleted (empty)", roomCode)
	}
}

// BroadcastToRoom отправляет сообщение всем пирам в комнате
func (rm *RoomManager) BroadcastToRoom(roomCode string, message SignalMessage, excludePeerID string) {
	room := rm.GetRoom(roomCode)
	if room == nil {
		return
	}

	room.Mutex.Lock()
	defer room.Mutex.Unlock()

	for peerID, peer := range room.Peers {
		if peerID != excludePeerID {
			if err := peer.Conn.WriteJSON(message); err != nil {
				log.Printf("Error sending message to peer %s: %v", peerID, err)
			}
		}
	}
}

// SendToPeer отправляет сообщение конкретному пиру
func (rm *RoomManager) SendToPeer(peerID string, message SignalMessage) bool {
	// Ищем пира во всех комнатах
	rm.mutex.RLock()
	defer rm.mutex.RUnlock()

	for _, room := range rm.rooms {
		room.Mutex.RLock()
		if peer, exists := room.Peers[peerID]; exists {
			room.Mutex.RUnlock()
			if err := peer.Conn.WriteJSON(message); err != nil {
				log.Printf("Error sending message to peer %s: %v", peerID, err)
				return false
			}
			return true
		}
		room.Mutex.RUnlock()
	}
	return false
}
