// Exit codes distinguish a successful socket operation from an explicit WFP denial
// and from failures that cannot establish enforcement.
#include <winsock2.h>
#include <windows.h>
#include <ws2tcpip.h>
#include <stdlib.h>
#include <string.h>

int main(int argc, char **argv)
{
    if (argc == 2 && strcmp(argv[1], "nul") == 0) {
        HANDLE device = CreateFileW(L"NUL", GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
        if (device == INVALID_HANDLE_VALUE)
            return GetLastError() == ERROR_ACCESS_DENIED ? 10 : 3;
        CloseHandle(device);
        return 0;
    }

    if (argc < 4 || argc > 5 ||
        (strcmp(argv[1], "tcp") && strcmp(argv[1], "udp") && strcmp(argv[1], "dns")) ||
        (argc == 5 && strcmp(argv[1], "udp")))
        return 2;

    char *end;
    unsigned long port = strtoul(argv[3], &end, 10);
    if (end == argv[3] || *end || port == 0 || port > 65535)
        return 2;

    WSADATA data;
    if (WSAStartup(MAKEWORD(2, 2), &data) != 0)
        return 3;

    struct sockaddr_storage address = {0};
    struct sockaddr_in *ipv4 = (struct sockaddr_in *)&address;
    struct sockaddr_in6 *ipv6 = (struct sockaddr_in6 *)&address;
    int size;
    if (InetPtonA(AF_INET, argv[2], &ipv4->sin_addr) == 1) {
        ipv4->sin_family = AF_INET;
        ipv4->sin_port = htons((unsigned short)port);
        size = sizeof(*ipv4);
    } else if (InetPtonA(AF_INET6, argv[2], &ipv6->sin6_addr) == 1) {
        ipv6->sin6_family = AF_INET6;
        ipv6->sin6_port = htons((unsigned short)port);
        size = sizeof(*ipv6);
    } else {
        WSACleanup();
        return 2;
    }

    int tcp = strcmp(argv[1], "tcp") == 0;
    int dns = strcmp(argv[1], "dns") == 0;
    SOCKET probe = socket(address.ss_family, tcp ? SOCK_STREAM : SOCK_DGRAM,
                          tcp ? IPPROTO_TCP : IPPROTO_UDP);
    if (probe == INVALID_SOCKET) {
        WSACleanup();
        return 3;
    }

    // A DNS request over a connected UDP socket provides a positive reply
    // for the host control without relying on the Windows DNS Client service.
    const unsigned char query[] = {
        0x48, 0x50, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0,
        7, 'e', 'x', 'a', 'm', 'p', 'l', 'e', 3, 'c', 'o', 'm', 0, 0, 1, 0, 1
    };
    const char *payload = argc == 5 ? argv[4] : "P";
    size_t payload_size = strlen(payload);
    if (payload_size == 0 || payload_size > 512) {
        closesocket(probe);
        WSACleanup();
        return 2;
    }
    int result = tcp || dns
        ? connect(probe, (struct sockaddr *)&address, size)
        : sendto(probe, payload, (int)payload_size, 0, (struct sockaddr *)&address, size);
    int exit_code = result != SOCKET_ERROR ? 0 : WSAGetLastError() == WSAEACCES ? 10 : 3;
    if (dns && exit_code == 0) {
        DWORD timeout = 3000;
        unsigned char reply[512];
        if (setsockopt(probe, SOL_SOCKET, SO_RCVTIMEO, (const char *)&timeout, sizeof(timeout)) == SOCKET_ERROR)
            exit_code = 3;
        else {
            result = send(probe, (const char *)query, sizeof(query), 0);
            if (result != SOCKET_ERROR)
                result = recv(probe, (char *)reply, sizeof(reply), 0);
            if (result == SOCKET_ERROR) {
                int error = WSAGetLastError();
                exit_code = error == WSAEACCES ? 10 : error == WSAETIMEDOUT ? 11 : 3;
            } else {
                exit_code = result >= 12 && reply[0] == query[0] && reply[1] == query[1] &&
                    (reply[2] & 0x80) != 0 ? 0 : 3;
            }
        }
    }
    closesocket(probe);
    WSACleanup();
    return exit_code;
}
