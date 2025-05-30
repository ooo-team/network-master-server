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

// Глобальная переменная-функция для получения IP клиента
var getClientIP = func(r *http.Request) string {
	host, _, err := net.SplitHostPort(r.RemoteAddr)
	if err != nil {
		return ""
	}
	return host
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

	host := getClientIP(r)
	if host == "" {
		http.Error(w, "Invalid remote address", http.StatusBadRequest)
		return
	}
	log.Printf("Host pidr: %s", host)

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

	response := map[string]string{"room_id": room.ID}
	responseJSON, _ := json.MarshalIndent(response, "", "  ")
	log.Printf("createRoom response:\n%s", string(responseJSON))

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(response)
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

	clientIP := getClientIP(r)
	if clientIP == "" {
		http.Error(w, "Invalid remote address", http.StatusBadRequest)
		return
	}
	log.Printf("Client IP (should be IPv4): %s", clientIP)

	s.mu.Lock()
	room, exists := s.rooms[roomID]
	if !exists {
		s.mu.Unlock()
		http.Error(w, "Room not found", http.StatusNotFound)
		return
	}

	if len(room.Clients) >= room.MaxClients {
		s.mu.Unlock()
		http.Error(w, "Room is full", http.StatusForbidden)
		return
	}

	room.Clients = append(room.Clients, clientIP)

	// Логируем информацию о комнате после подключения нового клиента
	roomJSON, _ := json.MarshalIndent(room, "", "  ")
	log.Printf("Client %s joined room %s. Room state:\n%s", clientIP, roomID, string(roomJSON))

	response := map[string]string{"status": "joined", "room_id": roomID}
	responseJSON, _ := json.MarshalIndent(response, "", "  ")
	log.Printf("joinRoom response:\n%s", string(responseJSON))

	s.mu.Unlock()

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(response)
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

	clientIP := getClientIP(r)
	if clientIP == "" {
		http.Error(w, "Invalid remote address", http.StatusBadRequest)
		return
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

	response := map[string]string{"status": "deleted", "room_id": roomID}
	responseJSON, _ := json.MarshalIndent(response, "", "  ")
	log.Printf("deleteRoom response:\n%s", string(responseJSON))

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(response)
}

func (s *Server) getRooms(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	roomID := r.URL.Query().Get("room_id")

	s.mu.RLock()
	defer s.mu.RUnlock()

	var response interface{}
	if roomID != "" {
		room, exists := s.rooms[roomID]
		if !exists {
			http.Error(w, "Room not found", http.StatusNotFound)
			return
		}
		response = room
	} else {
		// Если room_id не указан, возвращаем список всех комнат
		rooms := make([]*Room, 0, len(s.rooms))
		for _, room := range s.rooms {
			rooms = append(rooms, room)
		}
		response = rooms
	}

	// Логируем ответ перед отправкой
	responseJSON, _ := json.MarshalIndent(response, "", "  ")
	log.Printf("getRooms response:\n%s", string(responseJSON))

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(response)
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
