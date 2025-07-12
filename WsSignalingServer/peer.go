package main

import (
	"log"
	"sync"
)

// PeerManager управляет подключенными пирами
type PeerManager struct {
	peers map[string]*Peer
	mutex sync.RWMutex
}

// NewPeerManager создает новый менеджер пиров
func NewPeerManager() *PeerManager {
	pm := &PeerManager{
		peers: make(map[string]*Peer),
	}
	log.Printf("👥 PeerManager created")
	return pm
}

// AddPeer добавляет нового пира
func (pm *PeerManager) AddPeer(peer *Peer) {
	log.Printf("➕ PeerManager: Adding peer %s", peer.ID)
	pm.mutex.Lock()
	defer pm.mutex.Unlock()
	pm.peers[peer.ID] = peer
	log.Printf("✅ Peer %s successfully added to peer manager", peer.ID)
	log.Printf("📊 Total peers in manager: %d", len(pm.peers))
}

// RemovePeer удаляет пира
func (pm *PeerManager) RemovePeer(peerID string) {
	log.Printf("➖ PeerManager: Removing peer %s", peerID)
	pm.mutex.Lock()
	defer pm.mutex.Unlock()
	if peer, exists := pm.peers[peerID]; exists {
		log.Printf("🔌 Closing connection for peer %s", peerID)
		peer.Conn.Close()
		delete(pm.peers, peerID)
		log.Printf("✅ Peer %s successfully removed from peer manager", peerID)
		log.Printf("📊 Total peers in manager: %d", len(pm.peers))
	} else {
		log.Printf("⚠️  Peer %s not found in peer manager", peerID)
	}
}

// GetPeer возвращает пира по ID
func (pm *PeerManager) GetPeer(peerID string) *Peer {
	pm.mutex.RLock()
	defer pm.mutex.RUnlock()
	return pm.peers[peerID]
}

// GetAllPeers возвращает всех пиров
func (pm *PeerManager) GetAllPeers() map[string]*Peer {
	pm.mutex.RLock()
	defer pm.mutex.RUnlock()
	return pm.peers
}
