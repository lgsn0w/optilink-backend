<div align="center">
  <img src="assets/dashboard.jpg" alt="OptiLink Dashboard" width="100%">  

  <h1>OptiLink Core</h1>
  
  <h3>Controle e Telemetria para Home Lab (OptiPlex 7020)</h3>

  <p>
    <img src="https://img.shields.io/badge/OS-Ubuntu_Server_LTS-E95420?style=for-the-badge&logo=ubuntu&logoColor=white" alt="Ubuntu Server LTS">
    <img src="https://img.shields.io/badge/.NET-10.0_Preview-512bd4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10">
    <img src="https://img.shields.io/badge/Container-Docker-2496ed?style=for-the-badge&logo=docker&logoColor=white" alt="Docker">
    <img src="https://img.shields.io/badge/Realtime-SignalR-lightgrey?style=for-the-badge&logo=signalr" alt="SignalR">
  </p>
</div>

---

## Visão Geral

O **OptiLink** é o sistema que eu criei para gerenciar meu homelab  
(um OptiPlex 7020 rodando **Ubuntu Server LTS**).

Eu não queria instalar um monte de coisas pesadas como Prometheus ou Grafana
só para saber se meu Paperless estava rodando.  
Queria algo leve, que falasse direto com o Kernel e que me deixasse dormir
tranquilo sabendo que, se um container cair, o sistema levanta ele sozinho.

App mobile para upload de arquivos **a caminho**.

> *“Monitoramento direto da veia do Linux (`/proc`), sem intermediários.”*

---

## O que ele faz?

| Recurso | Descrição |
| :--- | :--- |
| **🔎 Leitura de Hardware** | Leitura direta de `/proc/meminfo` e `/proc/loadavg`. Zero overhead, resposta imediata. |
| **🐕 Docker Watchdog** | Vigia containers Docker. Se Nginx ou Paperless caírem, reinicia automaticamente em segundos. |
| **⚠️ Botão de Pânico** | Gatilho remoto que força a execução do exportador de backup do Paperless-ngx. |
| **📱 Matrix Dashboard** | Interface Web (e Mobile) escura, estilo terminal, com dados em tempo real via SignalR. |

---

## Arquitetura do Sistema

O backend roda **headless** e gerencia tudo via leitura direta do sistema e
comunicação por sockets.

```mermaid
graph TD
  A[Linux Kernel /proc] -->|Status Bruto| B[OptiLink Backend .NET 10]
  C[Docker Socket] <-->|Monitora & Reinicia| B
  B -->|Stream via SignalR| D[Web Dashboard]
  B -->|Stream via SignalR| E["App Android (Kotlin)"]

  D -.->|Gatilho de Backup| B
  B -.->|Executa Exporter| C
