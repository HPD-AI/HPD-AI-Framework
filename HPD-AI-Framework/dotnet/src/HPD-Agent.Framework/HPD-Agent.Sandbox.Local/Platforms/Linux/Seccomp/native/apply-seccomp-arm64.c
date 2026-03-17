/*
 * HPD Sandbox - Seccomp Helper (ARM64/aarch64)
 *
 * Pre-compile with:
 *   gcc -O2 -static -o apply-seccomp-arm64 apply-seccomp-arm64.c
 *
 * Or without static linking:
 *   gcc -O2 -o apply-seccomp-arm64 apply-seccomp-arm64.c
 */

#include <stdio.h>
#include <stdlib.h>
#include <stddef.h>
#include <unistd.h>
#include <errno.h>
#include <string.h>
#include <sys/prctl.h>
#include <linux/seccomp.h>
#include <linux/filter.h>
#include <linux/audit.h>

/* ARM64 specific */
#define SECCOMP_AUDIT_ARCH 0xc00000b7
#define SYS_SOCKET 198
#define SYS_SOCKETPAIR 199

#define AF_UNIX 1
#define SECCOMP_RET_ERRNO_EACCES (SECCOMP_RET_ERRNO | 13)

static struct sock_filter filter[] = {
    /* Load architecture */
    BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, arch)),
    /* Verify architecture */
    BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, SECCOMP_AUDIT_ARCH, 0, 7),

    /* Load syscall number */
    BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, nr)),
    /* Check if socket() */
    BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, SYS_SOCKET, 2, 0),
    /* Check if socketpair() */
    BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, SYS_SOCKETPAIR, 1, 0),
    /* Not a socket syscall - allow */
    BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_ALLOW),

    /* Load arg0 (domain) */
    BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, args[0])),
    /* Check if AF_UNIX */
    BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, AF_UNIX, 0, 1),
    /* Block with EACCES */
    BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_ERRNO_EACCES),
    /* Allow */
    BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_ALLOW),
};

static struct sock_fprog prog = {
    .len = sizeof(filter) / sizeof(filter[0]),
    .filter = filter,
};

int main(int argc, char *argv[]) {
    if (argc < 2) {
        fprintf(stderr, "HPD Sandbox Seccomp Helper (ARM64)\n");
        fprintf(stderr, "Usage: %s <command> [args...]\n", argv[0]);
        return 1;
    }

    if (prctl(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) != 0) {
        perror("prctl(PR_SET_NO_NEW_PRIVS)");
        return 1;
    }

    if (prctl(PR_SET_SECCOMP, SECCOMP_MODE_FILTER, &prog, 0, 0) != 0) {
        perror("prctl(PR_SET_SECCOMP)");
        return 1;
    }

    execvp(argv[1], &argv[1]);
    fprintf(stderr, "execvp(%s): %s\n", argv[1], strerror(errno));
    return 127;
}
