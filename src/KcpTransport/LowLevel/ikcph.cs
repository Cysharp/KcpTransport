#pragma warning disable CS8500
#pragma warning disable CS8981

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using IINT32 = int;
using IUINT32 = uint;
using size_t = nint;

namespace KcpTransport.LowLevel;

public unsafe struct IQUEUEHEAD
{
    public IQUEUEHEAD* next;
    public IQUEUEHEAD* prev;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void iqueue_init(IQUEUEHEAD* ptr)
    {
        ptr->next = ptr;
        ptr->prev = ptr;
    }

    // macro inlined
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe IKCPSEG* iqueue_entry(IQUEUEHEAD* ptr) // Type = IKCPSEG, member = node
    {
        // var a = (size_t)(&((IKCPSEG*)0)->node); // IOFFSETOF
        // var offset = Marshal.OffsetOf(typeof(IKCPSEG), "node");
        var offset = 0; // node offset is 0
        return (IKCPSEG*)((byte*)(IKCPSEG*)ptr - offset); // ICONTAINEROF
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool iqueue_is_empty(IQUEUEHEAD* entry)
    {
        return entry == entry->next;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void iqueue_del(IQUEUEHEAD* entry)
    {
        entry->next->prev = entry->prev;
        entry->prev->next = entry->next;
        entry->next = null;
        entry->prev = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void iqueue_del_init(IQUEUEHEAD* entry)
    {
        iqueue_del(entry);
        iqueue_init(entry);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void iqueue_add(IQUEUEHEAD* node, IQUEUEHEAD* head)
    {
        node->prev = head;
        node->next = head->next;
        head->next->prev = node;
        head->next = node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void iqueue_add_tail(IQUEUEHEAD* node, IQUEUEHEAD* head)
    {
        node->prev = head->prev;
        node->next = head;
        head->prev->next = node;
        head->prev = node;
    }
};

// Protocol
// https://github.com/skywind3000/kcp/blob/master/protocol.txt

// - conv: conversation id
// - cmd: command
// - frg: fragment count
// - wnd: window size
// - ts: timestamp
// - sn: serial number
// - una: un-acknowledged serial number

[StructLayout(LayoutKind.Sequential)]
public unsafe struct IKCPSEG
{
    public IQUEUEHEAD node;

    // in network data structure
    public IUINT32 conv; // 4B
    public IUINT32 cmd;  // 1B
    public IUINT32 frg;  // 1B
    public IUINT32 wnd;  // 2B
    public IUINT32 ts;   // 4B
    public IUINT32 sn;   // 4B
    public IUINT32 una;  // 4B
    public IUINT32 len;  // 4B

    public IUINT32 resendts;
    public IUINT32 rto;
    public IUINT32 fastack;
    public IUINT32 xmit;
    public fixed byte data[1]; // body, flexible array member
};

// void* user -> object user
public unsafe delegate int output_callback(byte* buf, int len, IKCPCB* kcp, object user);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate void writelog_callback(byte* log, IKCPCB* kcp, void* user);

[StructLayout(LayoutKind.Sequential)]
public unsafe struct IKCPCB
{
    public IUINT32 conv, mtu, mss, state;
    public IUINT32 snd_una, snd_nxt, rcv_nxt;
    public IUINT32 ts_recent, ts_lastack, ssthresh;
    public IINT32 rx_rttval, rx_srtt, rx_rto, rx_minrto;
    public IUINT32 snd_wnd, rcv_wnd, rmt_wnd, cwnd, probe;
    public IUINT32 current, interval, ts_flush, xmit;
    public IUINT32 nrcv_buf, nsnd_buf;
    public IUINT32 nrcv_que, nsnd_que;
    public IUINT32 nodelay, updated;
    public IUINT32 ts_probe, probe_wait;
    public IUINT32 dead_link, incr;
    public IQUEUEHEAD snd_queue;
    public IQUEUEHEAD rcv_queue;
    public IQUEUEHEAD snd_buf;
    public IQUEUEHEAD rcv_buf;
    public IUINT32* acklist;
    public IUINT32 ackcount;
    public IUINT32 ackblock;
    public object user; // void* -> object
    public byte* buffer;
    public int fastresend;
    public int fastlimit;
    public int nocwnd, stream;
    public int logmask;

    public output_callback output;
    public writelog_callback writelog;
};

internal static class KcpConfigurations
{
    public const IUINT32 IKCP_LOG_OUTPUT = 1;
    public const IUINT32 IKCP_LOG_INPUT = 2;
    public const IUINT32 IKCP_LOG_SEND = 4;
    public const IUINT32 IKCP_LOG_RECV = 8;
    public const IUINT32 IKCP_LOG_IN_DATA = 16;
    public const IUINT32 IKCP_LOG_IN_ACK = 32;
    public const IUINT32 IKCP_LOG_IN_PROBE = 64;
    public const IUINT32 IKCP_LOG_IN_WINS = 128;
    public const IUINT32 IKCP_LOG_OUT_DATA = 256;
    public const IUINT32 IKCP_LOG_OUT_ACK = 512;
    public const IUINT32 IKCP_LOG_OUT_PROBE = 1024;
    public const IUINT32 IKCP_LOG_OUT_WINS = 2048;
}
