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
	rm := &RoomManager{
		rooms: make(map[string]*Room),
	}
	log.Printf("🏠 RoomManager created")
	return rm
}

// getPeerIDs возвращает список ID пиров для логирования
func getPeerIDs(peers map[string]*Peer) []string {
	ids := make([]string, 0, len(peers))
	for id := range peers {
		ids = append(ids, id)
	}
	return ids
}

// CreateRoom создает новую комнату
func (rm *RoomManager) CreateRoom(roomCode string) *Room {
	log.Printf("🏗️  Creating new room: %s", roomCode)
	rm.mutex.Lock()
	defer rm.mutex.Unlock()

	room := &Room{
		Code:  roomCode,
		Peers: make(map[string]*Peer),
	}
	rm.rooms[roomCode] = room
	log.Printf("✅ Room %s created successfully", roomCode)
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
	log.Printf("🏠 RoomManager: Adding peer %s to room %s", peer.ID, roomCode)

	room := rm.GetRoom(roomCode)
	if room == nil {
		log.Printf("🏗️  Room %s doesn't exist, creating new room", roomCode)
		room = rm.CreateRoom(roomCode)
	} else {
		log.Printf("🏠 Room %s already exists, adding peer to existing room", roomCode)
	}

	room.Mutex.Lock()
	defer room.Mutex.Unlock()

	peer.RoomCode = roomCode
	room.Peers[peer.ID] = peer
	log.Printf("✅ Peer %s successfully joined room %s", peer.ID, roomCode)
	log.Printf("📊 Room %s now has %d peers: %v", roomCode, len(room.Peers), getPeerIDs(room.Peers))
	return true
}

// RemovePeerFromRoom удаляет пира из комнаты
func (rm *RoomManager) RemovePeerFromRoom(roomCode string, peerID string) {
	log.Printf("➖ RoomManager: Removing peer %s from room %s", peerID, roomCode)

	room := rm.GetRoom(roomCode)
	if room == nil {
		log.Printf("❌ Room %s not found, cannot remove peer %s", roomCode, peerID)
		return
	}

	room.Mutex.Lock()
	defer room.Mutex.Unlock()

	delete(room.Peers, peerID)
	log.Printf("✅ Peer %s removed from room %s", peerID, roomCode)
	log.Printf("📊 Room %s now has %d peers: %v", roomCode, len(room.Peers), getPeerIDs(room.Peers))

	// Если комната пустая, удаляем её
	if len(room.Peers) == 0 {
		rm.mutex.Lock()
		delete(rm.rooms, roomCode)
		rm.mutex.Unlock()
		log.Printf("🗑️  Room %s deleted (empty)", roomCode)
	}
}

// BroadcastToRoom отправляет сообщение всем пирам в комнате
func (rm *RoomManager) BroadcastToRoom(roomCode string, message SignalMessage, excludePeerID string) {
	log.Printf("📢 RoomManager: Broadcasting message to room %s (exclude: %s)", roomCode, excludePeerID)

	room := rm.GetRoom(roomCode)
	if room == nil {
		log.Printf("❌ Room %s not found, cannot broadcast", roomCode)
		return
	}

	room.Mutex.Lock()
	defer room.Mutex.Unlock()

	log.Printf("📊 Broadcasting to %d peers in room %s", len(room.Peers), roomCode)
	sentCount := 0
	for peerID, peer := range room.Peers {
		if peerID != excludePeerID {
			log.Printf("📤 Sending message to peer %s in room %s", peerID, roomCode)
			if err := peer.Conn.WriteJSON(message); err != nil {
				log.Printf("❌ Error sending message to peer %s: %v", peerID, err)
			} else {
				log.Printf("✅ Successfully sent message to peer %s", peerID)
				sentCount++
			}
		} else {
			log.Printf("⏭️  Skipping peer %s (excluded)", peerID)
		}
	}
	log.Printf("📈 Broadcast completed: sent to %d/%d peers in room %s", sentCount, len(room.Peers), roomCode)
}

// GetPeersInRoom возвращает список ID всех пиров в комнате
func (rm *RoomManager) GetPeersInRoom(roomCode string) []string {
	log.Printf("📋 RoomManager: Getting peers list for room %s", roomCode)

	room := rm.GetRoom(roomCode)
	if room == nil {
		log.Printf("❌ Room %s not found", roomCode)
		return []string{}
	}

	room.Mutex.RLock()
	defer room.Mutex.RUnlock()

	peerIDs := make([]string, 0, len(room.Peers))
	for peerID := range room.Peers {
		peerIDs = append(peerIDs, peerID)
	}

	log.Printf("📋 Room %s has %d peers: %v", roomCode, len(peerIDs), peerIDs)
	return peerIDs
}

// SendToPeer отправляет сообщение конкретному пиру
func (rm *RoomManager) SendToPeer(peerID string, message SignalMessage) bool {
	log.Printf("🎯 RoomManager: Looking for peer %s to send message", peerID)

	// Ищем пира во всех комнатах
	rm.mutex.RLock()
	defer rm.mutex.RUnlock()

	log.Printf("🔍 Searching through %d rooms for peer %s", len(rm.rooms), peerID)
	for roomCode, room := range rm.rooms {
		log.Printf("🔍 Checking room %s for peer %s", roomCode, peerID)
		room.Mutex.RLock()
		if peer, exists := room.Peers[peerID]; exists {
			room.Mutex.RUnlock()
			log.Printf("✅ Found peer %s in room %s, sending message", peerID, roomCode)
			if err := peer.Conn.WriteJSON(message); err != nil {
				log.Printf("❌ Error sending message to peer %s: %v", peerID, err)
				return false
			}
			log.Printf("✅ Successfully sent message to peer %s in room %s", peerID, roomCode)
			return true
		}
		room.Mutex.RUnlock()
		log.Printf("❌ Peer %s not found in room %s", peerID, roomCode)
	}
	log.Printf("❌ Peer %s not found in any room", peerID)
	return false
}
