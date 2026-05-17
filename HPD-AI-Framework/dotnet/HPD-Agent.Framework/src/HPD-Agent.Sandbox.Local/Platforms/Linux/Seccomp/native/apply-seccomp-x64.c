/*
 * HPD Sandbox - Seccomp Helper (x86_64)
 *
 * Pre-compile with:
 *   gcc -O2 -static -o apply-seccomp-x64 apply-seccomp-x64.c
 *
 * Or without static linking:
 *   gcc -O2 -o apply-seccomp-x64 apply-seccomp-x64.c
 */

#define _GNU_SOURCE

#include <stdio.h>
#include <stdlib.h>
#include <stddef.h>
#include <unistd.h>
#include <errno.h>
#include <string.h>
#include <signal.h>
#include <sched.h>
#include <sys/prctl.h>
#include <sys/mount.h>
#include <sys/stat.h>
#include <sys/wait.h>
#include <linux/seccomp.h>
#include <linux/filter.h>
#include <linux/audit.h>

/* x86_64 specific */
#define SECCOMP_AUDIT_ARCH 0xc000003e
#define SYS_SOCKET 41
#define SYS_SOCKETPAIR 53

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

static int apply_seccomp(void) {
    if (prctl(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) != 0) {
        perror("prctl(PR_SET_NO_NEW_PRIVS)");
        return -1;
    }

    if (prctl(PR_SET_SECCOMP, SECCOMP_MODE_FILTER, &prog, 0, 0) != 0) {
        perror("prctl(PR_SET_SECCOMP)");
        return -1;
    }

    return 0;
}

static void remount_proc_if_possible(void) {
    umount2("/proc", MNT_DETACH);
    mkdir("/proc", 0555);
    if (mount("proc", "/proc", "proc", MS_NOSUID | MS_NOEXEC | MS_NODEV, NULL) != 0) {
        fprintf(stderr, "warning: mount(/proc): %s\n", strerror(errno));
    }
}

static int exec_with_seccomp(char *argv[]) {
    if (apply_seccomp() != 0) {
        return 1;
    }

    execvp(argv[0], argv);
    fprintf(stderr, "execvp(%s): %s\n", argv[0], strerror(errno));
    return 127;
}

static int run_with_best_effort_reaper(char *argv[]) {
    if (unshare(CLONE_NEWNS | CLONE_NEWPID) != 0) {
        fprintf(stderr, "warning: unshare(CLONE_NEWNS|CLONE_NEWPID): %s\n", strerror(errno));
        return exec_with_seccomp(argv);
    }

    pid_t child = fork();
    if (child < 0) {
        perror("fork");
        return 1;
    }

    if (child == 0) {
        remount_proc_if_possible();
        return exec_with_seccomp(argv);
    }

    int status = 0;
    int child_status = 1;
    for (;;) {
        pid_t reaped = wait(&status);
        if (reaped < 0) {
            if (errno == EINTR) {
                continue;
            }
            break;
        }

        if (reaped == child) {
            child_status = status;
        }
    }

    if (WIFEXITED(child_status)) {
        return WEXITSTATUS(child_status);
    }
    if (WIFSIGNALED(child_status)) {
        return 128 + WTERMSIG(child_status);
    }
    return 1;
}

int main(int argc, char *argv[]) {
    if (argc < 2) {
        fprintf(stderr, "HPD Sandbox Seccomp Helper (x86_64)\n");
        fprintf(stderr, "Usage: %s <command> [args...]\n", argv[0]);
        return 1;
    }

    return run_with_best_effort_reaper(&argv[1]);
}
