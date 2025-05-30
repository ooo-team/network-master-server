// SPDX-FileCopyrightText: 2023 The Pion community <https://pion.ly>
// SPDX-License-Identifier: MIT

// Package main implements a simple TURN server
package main

import (
	"flag"
	"log"
	"net"
	"os"
	"os/signal"
	"regexp"
	"strconv"
	"syscall"

	"github.com/pion/turn/v4"
)

func main() {
	publicIP := flag.String("public-ip", "", "IP Address that TURN can be contacted by.")
	port := flag.Int("port", 3478, "Listening port.")
	users := flag.String("users", "", "List of username and password (e.g. \"user=pass,user=pass\")")
	realm := flag.String("realm", "pion.ly", "Realm (defaults to \"pion.ly\")")
	flag.Parse()

	if len(*publicIP) == 0 {
		log.Fatalf("'public-ip' is required")
	} else if len(*users) == 0 {
		log.Fatalf("'users' is required")
	}

	log.Printf("Запуск TURN сервера на %s:%d", *publicIP, *port)
	log.Printf("Используем realm: %s", *realm)

	// Create a UDP listener to pass into pion/turn
	// pion/turn itself doesn't allocate any UDP sockets, but lets the user pass them in
	// this allows us to add logging, storage or modify inbound/outbound traffic
	udpListener, err := net.ListenPacket("udp4", "0.0.0.0:"+strconv.Itoa(*port))
	if err != nil {
		log.Panicf("Failed to create TURN server listener: %s", err)
	}
	log.Printf("UDP listener создан на 0.0.0.0:%d", *port)

	// Cache -users flag for easy lookup later
	// If passwords are stored they should be saved to your DB hashed using turn.GenerateAuthKey
	usersMap := map[string][]byte{}
	for _, kv := range regexp.MustCompile(`(\w+)=(\w+)`).FindAllStringSubmatch(*users, -1) {
		usersMap[kv[1]] = turn.GenerateAuthKey(kv[1], *realm, kv[2])
		log.Printf("Добавлен пользователь: %s", kv[1])
	}

	server, err := turn.NewServer(turn.ServerConfig{
		Realm: *realm,
		// Set AuthHandler callback
		// This is called every time a user tries to authenticate with the TURN server
		// Return the key for that user, or false when no user is found
		AuthHandler: func(username string, realm string, srcAddr net.Addr) ([]byte, bool) { // nolint: revive
			log.Printf("Попытка аутентификации: username=%s, realm=%s, src=%s", username, realm, srcAddr.String())
			if key, ok := usersMap[username]; ok {
				log.Printf("Аутентификация успешна для пользователя: %s", username)
				return key, true
			}
			log.Printf("Аутентификация не удалась для пользователя: %s", username)
			return nil, false
		},
		// PacketConnConfigs is a list of UDP Listeners and the configuration around them
		PacketConnConfigs: []turn.PacketConnConfig{
			{
				PacketConn: udpListener,
				RelayAddressGenerator: &turn.RelayAddressGeneratorStatic{
					// Claim that we are listening on IP passed by user (This should be your Public IP)
					RelayAddress: net.ParseIP(*publicIP),
					// But actually be listening on every interface
					Address: "0.0.0.0",
				},
			},
		},
	})
	if err != nil {
		log.Panic(err)
	}

	log.Printf("TURN сервер запущен и готов к работе")

	// Block until user sends SIGINT or SIGTERM
	sigs := make(chan os.Signal, 1)
	signal.Notify(sigs, syscall.SIGINT, syscall.SIGTERM)
	<-sigs

	log.Printf("Получен сигнал завершения, закрываем сервер...")
	if err = server.Close(); err != nil {
		log.Panic(err)
	}
	log.Printf("Сервер успешно остановлен")
}
