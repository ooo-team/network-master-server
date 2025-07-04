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
	return &PeerManager{
		peers: make(map[string]*Peer),
	}
}

// AddPeer добавляет нового пира
func (pm *PeerManager) AddPeer(peer *Peer) {
	pm.mutex.Lock()
	defer pm.mutex.Unlock()
	pm.peers[peer.ID] = peer
	log.Printf("Peer %s connected", peer.ID)
}

// RemovePeer удаляет пира
func (pm *PeerManager) RemovePeer(peerID string) {
	pm.mutex.Lock()
	defer pm.mutex.Unlock()
	if peer, exists := pm.peers[peerID]; exists {
		peer.Conn.Close()
		delete(pm.peers, peerID)
		log.Printf("Peer %s disconnected", peerID)
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
