package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"log"
	"net"
	"net/http"
	"sync"

	"github.com/google/uuid"
)

type Room struct {
	ID         string   `json:"id"`
	Host       string   `json:"host"`
	MaxClients int      `json:"max_clients"`
	Clients    []string `json:"clients"`
}

type Server struct {
	rooms map[string]*Room
	mu    sync.RWMutex
}

func NewServer() *Server {
	return &Server{
		rooms: make(map[string]*Room),
	}
}

func (s *Server) createRoom(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	host := r.URL.Query().Get("host")
	if host == "" {
		http.Error(w, "Host parameter is required", http.StatusBadRequest)
		return
	}

	maxClients := r.URL.Query().Get("max_clients")
	if maxClients == "" {
		http.Error(w, "Max clients parameter is required", http.StatusBadRequest)
		return
	}

	var maxClientsInt int
	if _, err := fmt.Sscanf(maxClients, "%d", &maxClientsInt); err != nil {
		http.Error(w, "Invalid max_clients parameter", http.StatusBadRequest)
		return
	}

	room := &Room{
		ID:         uuid.New().String(),
		Host:       host,
		MaxClients: maxClientsInt,
		Clients:    []string{host},
	}

	s.mu.Lock()
	s.rooms[room.ID] = room
	s.mu.Unlock()

	log.Printf("Room created: %s", room.ID)

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(map[string]string{"room_id": room.ID})
}

func (s *Server) joinRoom(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPut {
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	roomID := r.URL.Query().Get("room_id")
	if roomID == "" {
		http.Error(w, "Room ID parameter is required", http.StatusBadRequest)
		return
	}

	clientIP := r.URL.Query().Get("client")
	if clientIP == "" {
		http.Error(w, "Client parameter is required", http.StatusBadRequest)
		return
	}

	s.mu.Lock()
	room, exists := s.rooms[roomID]
	if !exists {
		s.mu.Unlock()
		http.Error(w, "Room not found", http.StatusNotFound)
		return
	}

	// // Проверяем, не является ли клиент уже хостом
	// if clientIP == room.Host {
	// 	s.mu.Unlock()
	// 	http.Error(w, "Host is already in the room", http.StatusBadRequest)
	// 	return
	// }

	// // Проверяем, не находится ли клиент уже в комнате
	// for _, existingClient := range room.Clients {
	// 	if existingClient == clientIP {
	// 		s.mu.Unlock()
	// 		http.Error(w, "Client is already in the room", http.StatusBadRequest)
	// 		return
	// 	}
	// }

	if len(room.Clients) >= room.MaxClients {
		s.mu.Unlock()
		http.Error(w, "Room is full", http.StatusForbidden)
		return
	}

	room.Clients = append(room.Clients, clientIP)
	s.mu.Unlock()

	w.WriteHeader(http.StatusOK)
}

func (s *Server) deleteRoom(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodDelete {
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	roomID := r.URL.Query().Get("room_id")
	if roomID == "" {
		http.Error(w, "Room ID parameter is required", http.StatusBadRequest)
		return
	}

	clientIP := r.Header.Get("X-Real-IP")
	if clientIP == "" {
		host, _, err := net.SplitHostPort(r.RemoteAddr)
		if err != nil {
			http.Error(w, "Invalid remote address", http.StatusBadRequest)
			return
		}
		clientIP = host
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	room, exists := s.rooms[roomID]
	if !exists {
		http.Error(w, "Room not found", http.StatusNotFound)
		return
	}

	if room.Host != clientIP {
		http.Error(w, "Only room host can delete the room", http.StatusForbidden)
		return
	}

	delete(s.rooms, roomID)

	w.WriteHeader(http.StatusOK)
}

func (s *Server) getRooms(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	roomID := r.URL.Query().Get("room_id")

	s.mu.RLock()
	defer s.mu.RUnlock()

	if roomID != "" {
		room, exists := s.rooms[roomID]
		if !exists {
			http.Error(w, "Room not found", http.StatusNotFound)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(room)
		return
	}

	// Если room_id не указан, возвращаем список всех комнат
	rooms := make([]*Room, 0, len(s.rooms))
	for _, room := range s.rooms {
		rooms = append(rooms, room)
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(rooms)
}

func main() {
	port := flag.Int("port", 5312, "Port to run the server on")
	flag.Parse()

	server := NewServer()

	http.HandleFunc("/v1/rooms", func(w http.ResponseWriter, r *http.Request) {
		switch r.Method {
		case http.MethodGet:
			server.getRooms(w, r)
		case http.MethodPost:
			server.createRoom(w, r)
		case http.MethodPut:
			server.joinRoom(w, r)
		case http.MethodDelete:
			server.deleteRoom(w, r)
		default:
			http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		}
	})

	addr := fmt.Sprintf(":%d", *port)
	log.Printf("Server starting on %s", addr)
	if err := http.ListenAndServe(addr, nil); err != nil {
		log.Fatal(err)
	}
}
