#pragma warning disable CS8981

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KcpTransport.LowLevel
{
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
        public UInt32 conv; // 4B
        public UInt32 cmd; // 1B
        public UInt32 frg; // 1B
        public UInt32 wnd; // 2B
        public UInt32 ts; // 4B
        public UInt32 sn; // 4B
        public UInt32 una; // 4B
        public UInt32 len; // 4B

        public UInt32 resendts;
        public UInt32 rto;
        public UInt32 fastack;
        public UInt32 xmit;
        public fixed byte data[1]; // body, flexible array member
    };

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct IKCPCB
    {
        public UInt32 conv, mtu, mss, state;
        public UInt32 snd_una, snd_nxt, rcv_nxt;
        public UInt32 ts_recent, ts_lastack, ssthresh;
        public Int32 rx_rttval, rx_srtt, rx_rto, rx_minrto;
        public UInt32 snd_wnd, rcv_wnd, rmt_wnd, cwnd, probe;
        public UInt32 current, interval, ts_flush, xmit;
        public UInt32 nrcv_buf, nsnd_buf;
        public UInt32 nrcv_que, nsnd_que;
        public UInt32 nodelay, updated;
        public UInt32 ts_probe, probe_wait;
        public UInt32 dead_link, incr;
        public IQUEUEHEAD snd_queue;
        public IQUEUEHEAD rcv_queue;
        public IQUEUEHEAD snd_buf;
        public IQUEUEHEAD rcv_buf;
        public UInt32* acklist;
        public UInt32 ackcount;
        public UInt32 ackblock;
        public void* user;
        public byte* buffer;
        public int fastresend;
        public int fastlimit;
        public int nocwnd, stream;
        public int logmask;

        public delegate* managed<byte*, int, IKCPCB*, void*, int> output;
        public delegate* managed<string, IKCPCB*, void> writelog;
    };

    internal static class KcpConfigurations
    {
        public const uint IKCP_LOG_OUTPUT = 1;
        public const uint IKCP_LOG_INPUT = 2;
        public const uint IKCP_LOG_SEND = 4;
        public const uint IKCP_LOG_RECV = 8;
        public const uint IKCP_LOG_IN_DATA = 16;
        public const uint IKCP_LOG_IN_ACK = 32;
        public const uint IKCP_LOG_IN_PROBE = 64;
        public const uint IKCP_LOG_IN_WINS = 128;
        public const uint IKCP_LOG_OUT_DATA = 256;
        public const uint IKCP_LOG_OUT_ACK = 512;
        public const uint IKCP_LOG_OUT_PROBE = 1024;
        public const uint IKCP_LOG_OUT_WINS = 2048;
    }
}
