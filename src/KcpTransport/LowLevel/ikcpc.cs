#pragma warning disable CS8981

using System.Diagnostics;
using System.Runtime.CompilerServices;
using static KcpTransport.LowLevel.CMethods;
using static KcpTransport.LowLevel.IQUEUEHEAD;
using IINT32 = int;
using ikcpcb = KcpTransport.LowLevel.IKCPCB;
using IUINT16 = ushort;
using IUINT32 = uint;
using IUINT8 = byte;
using size_t = nint;

namespace KcpTransport.LowLevel;

public static unsafe class KcpMethods
{
    //=====================================================================
    // KCP BASIC
    //=====================================================================

    public const IUINT32 IKCP_RTO_NDL = 30;        // no delay min rto
    public const IUINT32 IKCP_RTO_MIN = 100;       // normal min rto
    public const IUINT32 IKCP_RTO_DEF = 200;       // RTO default
    public const IUINT32 IKCP_RTO_MAX = 60000;
    public const IUINT32 IKCP_CMD_PUSH = 81;       // cmd: push data
    public const IUINT32 IKCP_CMD_ACK = 82;        // cmd: ack
    public const IUINT32 IKCP_CMD_WASK = 83;       // cmd: window probe (ask)
    public const IUINT32 IKCP_CMD_WINS = 84;       // cmd: window size (tell)
    public const IUINT32 IKCP_ASK_SEND = 1;        // need to send IKCP_CMD_WASK
    public const IUINT32 IKCP_ASK_TELL = 2;        // need to send IKCP_CMD_WINS
    public const IUINT32 IKCP_WND_SND = 32;
    public const IUINT32 IKCP_WND_RCV = 128;       // must >= max fragment size
    public const IUINT32 IKCP_MTU_DEF = 1400;      // default MTU(Maximum Transmission Unit)
    public const IUINT32 IKCP_ACK_FAST = 3;
    public const IUINT32 IKCP_INTERVAL = 100;
    public const IUINT32 IKCP_OVERHEAD = 24;
    public const IUINT32 IKCP_DEADLINK = 20;
    public const IUINT32 IKCP_THRESH_INIT = 2;
    public const IUINT32 IKCP_THRESH_MIN = 2;
    public const IUINT32 IKCP_PROBE_INIT = 7000;       // 7 secs to probe window size
    public const IUINT32 IKCP_PROBE_LIMIT = 120000;    // up to 120 secs to probe window
    public const IUINT32 IKCP_FASTACK_LIMIT = 5;       // max times to trigger fastack


    //---------------------------------------------------------------------
    // encode / decode
    //---------------------------------------------------------------------

    /* encode 8 bits unsigned int */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte* ikcp_encode8u(byte* p, byte c)
    {
        *p++ = c;
        return p;
    }

    /* decode 8 bits unsigned int */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte* ikcp_decode8u(byte* p, byte* c)
    {
        *c = *p++;
        return p;
    }

    /* encode 16 bits unsigned int (lsb) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte* ikcp_encode16u(byte* p, ushort w)
    {
#if IWORDS_BIG_ENDIAN || IWORDS_MUST_ALIGN
	*(byte*)(p + 0) = (w & 255);
	*(byte*)(p + 1) = (w >> 8);
#else
        memcpy(p, &w, 2);

#endif
        p += 2;
        return p;
    }

    /* decode 16 bits unsigned int (lsb) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte* ikcp_decode16u(byte* p, ushort* w)
    {
#if IWORDS_BIG_ENDIAN || IWORDS_MUST_ALIGN
	*w = *(byte*)(p + 1);
	*w = *(byte*)(p + 0) + (*w << 8);
#else
        memcpy(w, p, 2);
#endif
        p += 2;
        return p;
    }

    /* encode 32 bits unsigned int (lsb) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte* ikcp_encode32u(byte* p, IUINT32 l)
    {
#if IWORDS_BIG_ENDIAN || IWORDS_MUST_ALIGN
	*(byte*)(p + 0) = (byte)((l >>  0) & 0xff);
	*(byte*)(p + 1) = (byte)((l >>  8) & 0xff);
	*(byte*)(p + 2) = (byte)((l >> 16) & 0xff);
	*(byte*)(p + 3) = (byte)((l >> 24) & 0xff);
#else
        memcpy(p, &l, 4);
#endif
        p += 4;
        return p;
    }

    /* decode 32 bits unsigned int (lsb) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte* ikcp_decode32u(byte* p, IUINT32* l)
    {
#if IWORDS_BIG_ENDIAN || IWORDS_MUST_ALIGN
	*l = *(byte*)(p + 3);
	*l = *(byte*)(p + 2) + (*l << 8);
	*l = *(byte*)(p + 1) + (*l << 8);
	*l = *(byte*)(p + 0) + (*l << 8);
#else
        memcpy(l, p, 4);
#endif
        p += 4;
        return p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static IUINT32 _imin_(IUINT32 a, IUINT32 b)
    {
        return a <= b ? a : b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static IUINT32 _imax_(IUINT32 a, IUINT32 b)
    {
        return a >= b ? a : b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static IUINT32 _ibound_(IUINT32 lower, IUINT32 middle, IUINT32 upper)
    {
        return _imin_(_imax_(lower, middle), upper);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static long _itimediff(IUINT32 later, IUINT32 earlier)
    {
        return (IINT32)(later - earlier);
    }

    //---------------------------------------------------------------------
    // manage segment
    //---------------------------------------------------------------------
    //typedef struct IKCPSEG IKCPSEG;
    //static void* (* ikcp_malloc_hook) (size_t) = null;
    //static void (* ikcp_free_hook) (void*) = null;

    // internal malloc
    static void* ikcp_malloc(size_t size)
    {
        //if (ikcp_malloc_hook)
        //    return ikcp_malloc_hook(size);
        return malloc(size);
    }

    // internal free
    static void ikcp_free(void* ptr)
    {
        //if (ikcp_free_hook)
        //{
        //    ikcp_free_hook(ptr);
        //}
        //else
        {
            free(ptr);
        }
    }

    // redefine allocator
    //void ikcp_allocator(void* (* new_malloc)(size_t), void (* new_free)(void*))
    //{
    //    ikcp_malloc_hook = new_malloc;
    //    ikcp_free_hook = new_free;
    //}

    // allocate a new kcp segment

    static IKCPSEG* ikcp_segment_new(ikcpcb* kcp, int size)
    {
        return (IKCPSEG*)ikcp_malloc(sizeof(IKCPSEG) + size);
    }

    // delete a segment
    static void ikcp_segment_delete(ikcpcb* kcp, IKCPSEG* seg)
    {
        ikcp_free(seg);
    }

    // write log
    //void ikcp_log(ikcpcb* kcp, int mask, byte* fmt, ...)
    //{
    //    char buffer[1024];
    //    va_list argptr;
    //    if ((mask & kcp->logmask) == 0 || kcp->writelog == 0) return;
    //    va_start(argptr, fmt);
    //    vsprintf(buffer, fmt, argptr);
    //    va_end(argptr);
    //    kcp->writelog(buffer, kcp, kcp->user);
    //}

    [Conditional("DEBUG")]
    static void ikcp_log(ikcpcb* kcp, string msg)
    {
        if (kcp->writelog == null) return;
        kcp->writelog(msg, kcp);
    }

    [Conditional("DEBUG")]
    static void ikcp_log<T1>(ikcpcb* kcp, string format, T1 arg1)
    {
        if (kcp->writelog == null) return;
        kcp->writelog(string.Format(format, arg1), kcp);
    }

    [Conditional("DEBUG")]
    static void ikcp_log<T1, T2>(ikcpcb* kcp, string format, T1 arg1, T2 arg2)
    {
        if (kcp->writelog == null) return;
        kcp->writelog(string.Format(format, arg1, arg2), kcp);
    }

    [Conditional("DEBUG")]
    static void ikcp_log<T1, T2, T3>(ikcpcb* kcp, string format, T1 arg1, T2 arg2, T3 arg3)
    {
        if (kcp->writelog == null) return;
        kcp->writelog(string.Format(format, arg1, arg2, arg3), kcp);
    }

    // check log mask
    //static int ikcp_canlog(const ikcpcb* kcp, int mask)
    //{
    //    if ((mask & kcp->logmask) == 0 || kcp->writelog == null) return 0;
    //    return 1;
    //}

    // output segment
    static int ikcp_output(ikcpcb* kcp, void* data, int size)
    {
        assert(kcp);
        // assert(kcp->output);
        //if (ikcp_canlog(kcp, IKCP_LOG_OUTPUT))
        //{
        //    ikcp_log(kcp, IKCP_LOG_OUTPUT, "[RO] %ld bytes", (long)size);
        //}
        ikcp_log(kcp, "[RO] {0} bytes", (long)size);
        if (size == 0) return 0;
        return kcp->output((byte*)data, size, kcp, kcp->user);
    }

    // output queue
    //void ikcp_qprint(byte* name, const struct IQUEUEHEAD *head)
    //{
    //#if 0
    //	const struct IQUEUEHEAD *p;
    //	printf("<%s>: [", name);
    //	for (p = head->next; p != head; p = p->next) {
    //		const IKCPSEG *seg = iqueue_entry(p, const IKCPSEG, node);
    //		printf("(%lu %d)", (unsigned long)seg->sn, (int)(seg->ts % 10000));
    //		if (p->next != head) printf(",");
    //	}
    //	printf("]\n");
    //#endif
    //}

    //---------------------------------------------------------------------
    // create a new kcpcb
    //---------------------------------------------------------------------
    public static ikcpcb* ikcp_create(IUINT32 conv, void* user)
    {
        ikcpcb* kcp = (ikcpcb*)ikcp_malloc(sizeof(ikcpcb));

        if (kcp == null) return null;
        kcp->conv = conv;
        kcp->user = user;
        kcp->snd_una = 0;
        kcp->snd_nxt = 0;
        kcp->rcv_nxt = 0;
        kcp->ts_recent = 0;
        kcp->ts_lastack = 0;
        kcp->ts_probe = 0;
        kcp->probe_wait = 0;
        kcp->snd_wnd = IKCP_WND_SND;
        kcp->rcv_wnd = IKCP_WND_RCV;
        kcp->rmt_wnd = IKCP_WND_RCV;
        kcp->cwnd = 0;
        kcp->incr = 0;
        kcp->probe = 0;
        kcp->mtu = IKCP_MTU_DEF;
        kcp->mss = kcp->mtu - IKCP_OVERHEAD;
        kcp->stream = 0;

        kcp->buffer = (byte*)ikcp_malloc((size_t)((kcp->mtu + IKCP_OVERHEAD) * 3));
        if (kcp->buffer == null)
        {
            ikcp_free(kcp);
            return null;
        }

        iqueue_init(&kcp->snd_queue);
        iqueue_init(&kcp->rcv_queue);
        iqueue_init(&kcp->snd_buf);
        iqueue_init(&kcp->rcv_buf);
        kcp->nrcv_buf = 0;
        kcp->nsnd_buf = 0;
        kcp->nrcv_que = 0;
        kcp->nsnd_que = 0;
        kcp->state = 0;
        kcp->acklist = null;
        kcp->ackblock = 0;
        kcp->ackcount = 0;
        kcp->rx_srtt = 0;
        kcp->rx_rttval = 0;
        kcp->rx_rto = (int)IKCP_RTO_DEF;
        kcp->rx_minrto = (int)IKCP_RTO_MIN;
        kcp->current = 0;
        kcp->interval = IKCP_INTERVAL;
        kcp->ts_flush = IKCP_INTERVAL;
        kcp->nodelay = 0;
        kcp->updated = 0;
        kcp->logmask = 0;
        kcp->ssthresh = IKCP_THRESH_INIT;
        kcp->fastresend = 0;
        kcp->fastlimit = (int)IKCP_FASTACK_LIMIT;
        kcp->nocwnd = 0;
        kcp->xmit = 0;
        kcp->dead_link = IKCP_DEADLINK;
        kcp->output = null!;
        kcp->writelog = null!;

        return kcp;
    }


    //---------------------------------------------------------------------
    // release a new kcpcb
    //---------------------------------------------------------------------
    public static void ikcp_release(ikcpcb* kcp)
    {
        assert(kcp);
        if (kcp != null)
        {
            IKCPSEG* seg;
            while (!iqueue_is_empty(&kcp->snd_buf))
            {
                seg = iqueue_entry(kcp->snd_buf.next);
                iqueue_del(&seg->node);
                ikcp_segment_delete(kcp, seg);
            }
            while (!iqueue_is_empty(&kcp->rcv_buf))
            {
                seg = iqueue_entry(kcp->rcv_buf.next);
                iqueue_del(&seg->node);
                ikcp_segment_delete(kcp, seg);
            }
            while (!iqueue_is_empty(&kcp->snd_queue))
            {
                seg = iqueue_entry(kcp->snd_queue.next);
                iqueue_del(&seg->node);
                ikcp_segment_delete(kcp, seg);
            }
            while (!iqueue_is_empty(&kcp->rcv_queue))
            {
                seg = iqueue_entry(kcp->rcv_queue.next);
                iqueue_del(&seg->node);
                ikcp_segment_delete(kcp, seg);
            }
            if (kcp->buffer != null)
            {
                ikcp_free(kcp->buffer);
            }
            if (kcp->acklist != null)
            {
                ikcp_free(kcp->acklist);
            }

            kcp->nrcv_buf = 0;
            kcp->nsnd_buf = 0;
            kcp->nrcv_que = 0;
            kcp->nsnd_que = 0;
            kcp->ackcount = 0;
            kcp->buffer = null;
            kcp->acklist = null;
            ikcp_free(kcp);
        }
    }


    //---------------------------------------------------------------------
    // set output callback, which will be invoked by kcp
    //---------------------------------------------------------------------
    public static void ikcp_setoutput(ikcpcb* kcp, delegate* managed<byte*, int, IKCPCB*, void*, int> output)
    {
        kcp->output = output;
    }

    //---------------------------------------------------------------------
    // user/upper level recv: returns size, returns below zero for EAGAIN
    //---------------------------------------------------------------------
    public static int ikcp_recv(ikcpcb* kcp, byte* buffer, int len)
    {
        // struct IQUEUEHEAD *p;
        int ispeek = len < 0 ? 1 : 0;
        int peeksize;
        int recover = 0;
        IKCPSEG* seg;
        assert(kcp);

        if (iqueue_is_empty(&kcp->rcv_queue))
            return -1;
        if (len < 0) len = -len;
        peeksize = ikcp_peeksize(kcp);

        if (peeksize < 0)
            return -2;
        if (peeksize > len)
            return -3;

        if (kcp->nrcv_que >= kcp->rcv_wnd)
            recover = 1;

        // merge fragment
        var p = kcp->rcv_queue.next;
        for (len = 0; p != &kcp->rcv_queue;)
        {
            int fragment;
            seg = iqueue_entry(p);
            p = p->next;

            if (buffer != null)
            {
                memcpy(buffer, seg->data, (int)seg->len);
                buffer += seg->len;
            }

            len += (int)seg->len;
            fragment = (int)seg->frg;

            ikcp_log(kcp, "recv sn={0}", seg->sn);

            if (ispeek == 0)
            {
                iqueue_del(&seg->node);
                ikcp_segment_delete(kcp, seg);
                kcp->nrcv_que--;
            }

            if (fragment == 0)
                break;
        }

        assert(len == peeksize);

        // move available data from rcv_buf -> rcv_queue
        while (!iqueue_is_empty(&kcp->rcv_buf))
        {
            seg = iqueue_entry(kcp->rcv_buf.next);
            if (seg->sn == kcp->rcv_nxt && kcp->nrcv_que < kcp->rcv_wnd)
            {
                iqueue_del(&seg->node);
                kcp->nrcv_buf--;
                iqueue_add_tail(&seg->node, &kcp->rcv_queue);
                kcp->nrcv_que++;
                kcp->rcv_nxt++;
            }
            else
            {
                break;
            }
        }

        // fast recover
        if (kcp->nrcv_que < kcp->rcv_wnd && recover != 0)
        {
            // ready to send back IKCP_CMD_WINS in ikcp_flush
            // tell remote my window size
            kcp->probe |= IKCP_ASK_TELL;
        }

        return len;
    }


    //---------------------------------------------------------------------
    // peek data size
    //---------------------------------------------------------------------
    public static int ikcp_peeksize(ikcpcb* kcp)
    {
        IQUEUEHEAD* p;
        IKCPSEG* seg;
        int length = 0;
        assert(kcp);

        if (iqueue_is_empty(&kcp->rcv_queue)) return -1;

        seg = iqueue_entry(kcp->rcv_queue.next);
        if (seg->frg == 0) return (int)seg->len;

        if (kcp->nrcv_que < seg->frg + 1) return -1;

        for (p = kcp->rcv_queue.next; p != &kcp->rcv_queue; p = p->next)
        {
            seg = iqueue_entry(p);
            length += (int)seg->len;
            if (seg->frg == 0) break;
        }

        return length;
    }


    //---------------------------------------------------------------------
    // user/upper level send, returns below zero for error
    //---------------------------------------------------------------------
    public static int ikcp_send(ikcpcb* kcp, byte* buffer, int len)
    {
        IKCPSEG* seg;
        int count, i;
        int sent = 0;

        assert(kcp->mss > 0);
        if (len < 0) return -1;

        // append to previous segment in streaming mode (if possible)
        if (kcp->stream != 0)
        {
            if (!iqueue_is_empty(&kcp->snd_queue))
            {
                IKCPSEG* old = iqueue_entry(kcp->snd_queue.prev);
                if (old->len < kcp->mss)
                {
                    int capacity = (int)kcp->mss - (int)old->len;
                    int extend = len < capacity ? len : capacity;
                    seg = ikcp_segment_new(kcp, (int)old->len + extend);
                    assert(seg);
                    if (seg == null)
                    {
                        return -2;
                    }
                    iqueue_add_tail(&seg->node, &kcp->snd_queue);
                    memcpy(seg->data, old->data, (int)old->len);
                    if (buffer != null)
                    {
                        memcpy(seg->data + old->len, buffer, extend);
                        buffer += extend;
                    }
                    seg->len = old->len + (uint)extend;
                    seg->frg = 0;
                    len -= extend;
                    iqueue_del_init(&old->node);
                    ikcp_segment_delete(kcp, old);
                    sent = extend;
                }
            }
            if (len <= 0)
            {
                return sent;
            }
        }

        if (len <= (int)kcp->mss) count = 1;
        else count = (int)((len + kcp->mss - 1) / kcp->mss);

        // fix: https://github.com/skywind3000/kcp/pull/291
        // if (count >= (int)IKCP_WND_RCV)
        if (count >= (int)kcp->rcv_wnd)
        {
            if (kcp->stream != 0 && sent > 0)
                return sent;
            return -2;
        }

        if (count == 0) count = 1;

        // fragment
        for (i = 0; i < count; i++)
        {
            int size = len > (int)kcp->mss ? (int)kcp->mss : len;
            seg = ikcp_segment_new(kcp, size);
            assert(seg);
            if (seg == null)
            {
                return -2;
            }
            if (buffer != null && len > 0)
            {
                memcpy(seg->data, buffer, size);
            }
            seg->len = (uint)size;
            seg->frg = kcp->stream == 0 ? (uint)(count - i - 1) : 0;
            iqueue_init(&seg->node);
            iqueue_add_tail(&seg->node, &kcp->snd_queue);
            kcp->nsnd_que++;
            if (buffer != null)
            {
                buffer += size;
            }
            len -= size;
            sent += size;
        }

        return sent;
    }


    //---------------------------------------------------------------------
    // parse ack
    //---------------------------------------------------------------------
    static void ikcp_update_ack(ikcpcb* kcp, IINT32 rtt)
    {
        IINT32 rto = 0;
        if (kcp->rx_srtt == 0)
        {
            kcp->rx_srtt = rtt;
            kcp->rx_rttval = rtt / 2;
        }
        else
        {
            long delta = rtt - kcp->rx_srtt;
            if (delta < 0) delta = -delta;
            kcp->rx_rttval = (int)((3 * kcp->rx_rttval + delta) / 4);
            kcp->rx_srtt = (7 * kcp->rx_srtt + rtt) / 8;
            if (kcp->rx_srtt < 1) kcp->rx_srtt = 1;
        }
        rto = (int)(kcp->rx_srtt + _imax_(kcp->interval, (uint)(4 * kcp->rx_rttval)));
        kcp->rx_rto = (int)_ibound_((uint)kcp->rx_minrto, (uint)rto, IKCP_RTO_MAX);
    }

    static void ikcp_shrink_buf(ikcpcb* kcp)
    {
        IQUEUEHEAD* p = kcp->snd_buf.next;
        if (p != &kcp->snd_buf)
        {
            IKCPSEG* seg = iqueue_entry(p);
            kcp->snd_una = seg->sn;
        }
        else
        {
            kcp->snd_una = kcp->snd_nxt;
        }
    }

    static void ikcp_parse_ack(ikcpcb* kcp, IUINT32 sn)
    {
        IQUEUEHEAD* p, next;
        if (_itimediff(sn, kcp->snd_una) < 0 || _itimediff(sn, kcp->snd_nxt) >= 0)
            return;

        for (p = kcp->snd_buf.next; p != &kcp->snd_buf; p = next)
        {
            IKCPSEG* seg = iqueue_entry(p);
            next = p->next;
            if (sn == seg->sn)
            {
                iqueue_del(p);
                ikcp_segment_delete(kcp, seg);
                kcp->nsnd_buf--;
                break;
            }
            if (_itimediff(sn, seg->sn) < 0)
            {
                break;
            }
        }
    }

    static void ikcp_parse_una(ikcpcb* kcp, IUINT32 una)
    {
        IQUEUEHEAD* p, next;
        for (p = kcp->snd_buf.next; p != &kcp->snd_buf; p = next)
        {
            IKCPSEG* seg = iqueue_entry(p);
            next = p->next;
            if (_itimediff(una, seg->sn) > 0)
            {
                iqueue_del(p);
                ikcp_segment_delete(kcp, seg);
                kcp->nsnd_buf--;
            }
            else
            {
                break;
            }
        }
    }

    static void ikcp_parse_fastack(ikcpcb* kcp, IUINT32 sn, IUINT32 ts)
    {
        IQUEUEHEAD* p, next;
        if (_itimediff(sn, kcp->snd_una) < 0 || _itimediff(sn, kcp->snd_nxt) >= 0)
            return;

        for (p = kcp->snd_buf.next; p != &kcp->snd_buf; p = next)
        {
            IKCPSEG* seg = iqueue_entry(p);
            next = p->next;
            if (_itimediff(sn, seg->sn) < 0)
            {
                break;
            }
            else if (sn != seg->sn)
            {
#if IKCP_FASTACK_CONSERVE
                seg->fastack++;
#else
                if (_itimediff(ts, seg->ts) >= 0)
                    seg->fastack++;
#endif
            }
        }
    }


    //---------------------------------------------------------------------
    // ack append
    //---------------------------------------------------------------------
    static void ikcp_ack_push(ikcpcb* kcp, IUINT32 sn, IUINT32 ts)
    {
        IUINT32 newsize = kcp->ackcount + 1;
        IUINT32* ptr;

        if (newsize > kcp->ackblock)
        {
            IUINT32* acklist;
            IUINT32 newblock;

            for (newblock = 8; newblock < newsize; newblock <<= 1) ;
            acklist = (IUINT32*)ikcp_malloc((size_t)(newblock * sizeof(IUINT32) * 2));

            if (acklist == null)
            {
                assert(acklist != null);
                abort();
            }

            if (kcp->acklist != null)
            {
                IUINT32 x;
                for (x = 0; x < kcp->ackcount; x++)
                {
                    acklist[x * 2 + 0] = kcp->acklist[x * 2 + 0];
                    acklist[x * 2 + 1] = kcp->acklist[x * 2 + 1];
                }
                ikcp_free(kcp->acklist);
            }

            kcp->acklist = acklist;
            kcp->ackblock = newblock;
        }

        ptr = &kcp->acklist[kcp->ackcount * 2];
        ptr[0] = sn;
        ptr[1] = ts;
        kcp->ackcount++;
    }

    static void ikcp_ack_get(ikcpcb* kcp, int p, IUINT32* sn, IUINT32* ts)
    {
        if (sn != null) sn[0] = kcp->acklist[p * 2 + 0];
        if (ts != null) ts[0] = kcp->acklist[p * 2 + 1];
    }


    //---------------------------------------------------------------------
    // parse data
    //---------------------------------------------------------------------
    static void ikcp_parse_data(ikcpcb* kcp, IKCPSEG* newseg)
    {
        IQUEUEHEAD* p, prev;
        IUINT32 sn = newseg->sn;
        int repeat = 0;

        if (_itimediff(sn, kcp->rcv_nxt + kcp->rcv_wnd) >= 0 ||
            _itimediff(sn, kcp->rcv_nxt) < 0)
        {
            ikcp_segment_delete(kcp, newseg);
            return;
        }

        for (p = kcp->rcv_buf.prev; p != &kcp->rcv_buf; p = prev)
        {
            IKCPSEG* seg = iqueue_entry(p);
            prev = p->prev;
            if (seg->sn == sn)
            {
                repeat = 1;
                break;
            }
            if (_itimediff(sn, seg->sn) > 0)
            {
                break;
            }
        }

        if (repeat == 0)
        {
            iqueue_init(&newseg->node);
            iqueue_add(&newseg->node, p);
            kcp->nrcv_buf++;
        }
        else
        {
            ikcp_segment_delete(kcp, newseg);
        }

#if false
	ikcp_qprint("rcvbuf", &kcp->rcv_buf);
	printf("rcv_nxt=%lu\n", kcp->rcv_nxt);
#endif

        // move available data from rcv_buf -> rcv_queue
        while (!iqueue_is_empty(&kcp->rcv_buf))
        {
            IKCPSEG* seg = iqueue_entry(kcp->rcv_buf.next);
            if (seg->sn == kcp->rcv_nxt && kcp->nrcv_que < kcp->rcv_wnd)
            {
                iqueue_del(&seg->node);
                kcp->nrcv_buf--;
                iqueue_add_tail(&seg->node, &kcp->rcv_queue);
                kcp->nrcv_que++;
                kcp->rcv_nxt++;
            }
            else
            {
                break;
            }
        }

#if false
	ikcp_qprint("queue", &kcp->rcv_queue);
	printf("rcv_nxt=%lu\n", kcp->rcv_nxt);
#endif

#if true
        //	printf("snd(buf=%d, queue=%d)\n", kcp->nsnd_buf, kcp->nsnd_que);
        //	printf("rcv(buf=%d, queue=%d)\n", kcp->nrcv_buf, kcp->nrcv_que);
#endif
    }


    //---------------------------------------------------------------------
    // input data
    //---------------------------------------------------------------------
    public static int ikcp_input(ikcpcb* kcp, byte* data, long size)
    {
        IUINT32 prev_una = kcp->snd_una;
        IUINT32 maxack = 0, latest_ts = 0;
        int flag = 0;

        //if (ikcp_canlog(kcp, IKCP_LOG_INPUT))
        //{
        //    ikcp_log(kcp, IKCP_LOG_INPUT, "[RI] %d bytes", (int)size);
        //}
        ikcp_log(kcp, "[RI] {0} bytes", (int)size);

        if (data == null || (int)size < (int)IKCP_OVERHEAD) return -1;

        while (true)
        {
            IUINT32 ts, sn, len, una, conv;
            IUINT16 wnd;
            IUINT8 cmd, frg;
            IKCPSEG* seg;

            if (size < (int)IKCP_OVERHEAD) break;

            data = ikcp_decode32u(data, &conv);
            if (conv != kcp->conv) return -1;

            data = ikcp_decode8u(data, &cmd);
            data = ikcp_decode8u(data, &frg);
            data = ikcp_decode16u(data, &wnd);
            data = ikcp_decode32u(data, &ts);
            data = ikcp_decode32u(data, &sn);
            data = ikcp_decode32u(data, &una);
            data = ikcp_decode32u(data, &len);

            size -= IKCP_OVERHEAD;

            if (size < len || (int)len < 0) return -2;

            if (cmd != IKCP_CMD_PUSH && cmd != IKCP_CMD_ACK &&
                cmd != IKCP_CMD_WASK && cmd != IKCP_CMD_WINS)
                return -3;

            kcp->rmt_wnd = wnd;
            ikcp_parse_una(kcp, una);
            ikcp_shrink_buf(kcp);

            if (cmd == IKCP_CMD_ACK)
            {
                if (_itimediff(kcp->current, ts) >= 0)
                {
                    ikcp_update_ack(kcp, (int)_itimediff(kcp->current, ts));
                }
                ikcp_parse_ack(kcp, sn);
                ikcp_shrink_buf(kcp);
                if (flag == 0)
                {
                    flag = 1;
                    maxack = sn;
                    latest_ts = ts;
                }
                else
                {
                    if (_itimediff(sn, maxack) > 0)
                    {
# if IKCP_FASTACK_CONSERVE
                        maxack = sn;
                        latest_ts = ts;
#else
                        if (_itimediff(ts, latest_ts) > 0)
                        {
                            maxack = sn;
                            latest_ts = ts;
                        }
#endif
                    }
                }
                //           if (ikcp_canlog(kcp, IKCP_LOG_IN_ACK))
                //           {
                //               ikcp_log(kcp, IKCP_LOG_IN_ACK,
                //                   "input ack: sn=%lu rtt=%ld rto=%ld", (unsigned long)sn, 
                //(long)_itimediff(kcp->current, ts),
                //(long)kcp->rx_rto);
                //           }

                ikcp_log(kcp, "input ack: sn={0} rtt={1} rto={2}", sn, (long)_itimediff(kcp->current, ts), (long)kcp->rx_rto);

            }
            else if (cmd == IKCP_CMD_PUSH)
            {
                //if (ikcp_canlog(kcp, IKCP_LOG_IN_DATA))
                //{
                //    ikcp_log(kcp, IKCP_LOG_IN_DATA,
                //        "input psh: sn=%lu ts=%lu", (unsigned long)sn, (unsigned long)ts);
                //}
                ikcp_log(kcp, "input psh: sn={0} ts={1}", sn, ts);
                if (_itimediff(sn, kcp->rcv_nxt + kcp->rcv_wnd) < 0)
                {
                    ikcp_ack_push(kcp, sn, ts);
                    if (_itimediff(sn, kcp->rcv_nxt) >= 0)
                    {
                        seg = ikcp_segment_new(kcp, (int)len);
                        seg->conv = conv;
                        seg->cmd = cmd;
                        seg->frg = frg;
                        seg->wnd = wnd;
                        seg->ts = ts;
                        seg->sn = sn;
                        seg->una = una;
                        seg->len = len;

                        if (len > 0)
                        {
                            memcpy(seg->data, data, (int)len);
                        }

                        ikcp_parse_data(kcp, seg);
                    }
                }
            }
            else if (cmd == IKCP_CMD_WASK)
            {
                // ready to send back IKCP_CMD_WINS in ikcp_flush
                // tell remote my window size
                kcp->probe |= IKCP_ASK_TELL;
                //if (ikcp_canlog(kcp, IKCP_LOG_IN_PROBE))
                //{
                //    ikcp_log(kcp, IKCP_LOG_IN_PROBE, "input probe");
                //}
                ikcp_log(kcp, "input probe");
            }
            else if (cmd == IKCP_CMD_WINS)
            {
                // do nothing
                //if (ikcp_canlog(kcp, IKCP_LOG_IN_WINS))
                //{
                //    ikcp_log(kcp, IKCP_LOG_IN_WINS,
                //        "input wins: %lu", (unsigned long)(wnd));
                //}
                ikcp_log(kcp, "input wins: {0}", (wnd));
            }
            else
            {
                return -3;
            }

            data += len;
            size -= len;
        }

        if (flag != 0)
        {
            ikcp_parse_fastack(kcp, maxack, latest_ts);
        }

        if (_itimediff(kcp->snd_una, prev_una) > 0)
        {
            if (kcp->cwnd < kcp->rmt_wnd)
            {
                IUINT32 mss = kcp->mss;
                if (kcp->cwnd < kcp->ssthresh)
                {
                    kcp->cwnd++;
                    kcp->incr += mss;
                }
                else
                {
                    if (kcp->incr < mss) kcp->incr = mss;
                    kcp->incr += mss * mss / kcp->incr + mss / 16;
                    if ((kcp->cwnd + 1) * mss <= kcp->incr)
                    {
#if true
                        kcp->cwnd = (kcp->incr + mss - 1) / (mss > 0 ? mss : 1);
#else
                        kcp->cwnd++;
#endif
                    }
                }
                if (kcp->cwnd > kcp->rmt_wnd)
                {
                    kcp->cwnd = kcp->rmt_wnd;
                    kcp->incr = kcp->rmt_wnd * mss;
                }
            }
        }

        return 0;
    }


    //---------------------------------------------------------------------
    // ikcp_encode_seg
    //---------------------------------------------------------------------
    static byte* ikcp_encode_seg(byte* ptr, IKCPSEG* seg)
    {
        ptr = ikcp_encode32u(ptr, seg->conv);
        ptr = ikcp_encode8u(ptr, (IUINT8)seg->cmd);
        ptr = ikcp_encode8u(ptr, (IUINT8)seg->frg);
        ptr = ikcp_encode16u(ptr, (IUINT16)seg->wnd);
        ptr = ikcp_encode32u(ptr, seg->ts);
        ptr = ikcp_encode32u(ptr, seg->sn);
        ptr = ikcp_encode32u(ptr, seg->una);
        ptr = ikcp_encode32u(ptr, seg->len);
        return ptr;
    }

    static int ikcp_wnd_unused(ikcpcb* kcp)
    {
        if (kcp->nrcv_que < kcp->rcv_wnd)
        {
            return (int)(kcp->rcv_wnd - kcp->nrcv_que);
        }
        return 0;
    }


    //---------------------------------------------------------------------
    // ikcp_flush
    //---------------------------------------------------------------------
    public static void ikcp_flush(ikcpcb* kcp)
    {
        IUINT32 current = kcp->current;
        byte* buffer = kcp->buffer;
        byte* ptr = buffer;
        int count, size, i;
        IUINT32 resent, cwnd;
        IUINT32 rtomin;

        IQUEUEHEAD* p;
        int change = 0;
        int lost = 0;
        IKCPSEG seg;

        // 'ikcp_update' haven't been called. 
        if (kcp->updated == 0) return;

        seg.conv = kcp->conv;
        seg.cmd = IKCP_CMD_ACK;
        seg.frg = 0;
        seg.wnd = (uint)ikcp_wnd_unused(kcp);
        seg.una = kcp->rcv_nxt;
        seg.len = 0;
        seg.sn = 0;
        seg.ts = 0;

        // flush acknowledges
        count = (int)kcp->ackcount;
        for (i = 0; i < count; i++)
        {
            size = (int)(ptr - buffer);
            if (size + (int)IKCP_OVERHEAD > (int)kcp->mtu)
            {
                ikcp_output(kcp, buffer, size);
                ptr = buffer;
            }
            ikcp_ack_get(kcp, i, &seg.sn, &seg.ts);
            ptr = ikcp_encode_seg(ptr, &seg);
        }

        kcp->ackcount = 0;

        // probe window size (if remote window size equals zero)
        if (kcp->rmt_wnd == 0)
        {
            if (kcp->probe_wait == 0)
            {
                kcp->probe_wait = IKCP_PROBE_INIT;
                kcp->ts_probe = kcp->current + kcp->probe_wait;
            }
            else
            {
                if (_itimediff(kcp->current, kcp->ts_probe) >= 0)
                {
                    if (kcp->probe_wait < IKCP_PROBE_INIT)
                        kcp->probe_wait = IKCP_PROBE_INIT;
                    kcp->probe_wait += kcp->probe_wait / 2;
                    if (kcp->probe_wait > IKCP_PROBE_LIMIT)
                        kcp->probe_wait = IKCP_PROBE_LIMIT;
                    kcp->ts_probe = kcp->current + kcp->probe_wait;
                    kcp->probe |= IKCP_ASK_SEND;
                }
            }
        }
        else
        {
            kcp->ts_probe = 0;
            kcp->probe_wait = 0;
        }

        // flush window probing commands
        if (kcp->probe != 0 & IKCP_ASK_SEND != 0)
        {
            seg.cmd = IKCP_CMD_WASK;
            size = (int)(ptr - buffer);
            if (size + (int)IKCP_OVERHEAD > (int)kcp->mtu)
            {
                ikcp_output(kcp, buffer, size);
                ptr = buffer;
            }
            ptr = ikcp_encode_seg(ptr, &seg);
        }

        // flush window probing commands
        if (kcp->probe != 0 & IKCP_ASK_TELL != 0)
        {
            seg.cmd = IKCP_CMD_WINS;
            size = (int)(ptr - buffer);
            if (size + (int)IKCP_OVERHEAD > (int)kcp->mtu)
            {
                ikcp_output(kcp, buffer, size);
                ptr = buffer;
            }
            ptr = ikcp_encode_seg(ptr, &seg);
        }

        kcp->probe = 0;

        // calculate window size
        cwnd = _imin_(kcp->snd_wnd, kcp->rmt_wnd);
        if (kcp->nocwnd == 0) cwnd = _imin_(kcp->cwnd, cwnd);

        // move data from snd_queue to snd_buf
        while (_itimediff(kcp->snd_nxt, kcp->snd_una + cwnd) < 0)
        {
            IKCPSEG* newseg;
            if (iqueue_is_empty(&kcp->snd_queue)) break;

            newseg = iqueue_entry(kcp->snd_queue.next);

            iqueue_del(&newseg->node);
            iqueue_add_tail(&newseg->node, &kcp->snd_buf);
            kcp->nsnd_que--;
            kcp->nsnd_buf++;

            newseg->conv = kcp->conv;
            newseg->cmd = IKCP_CMD_PUSH;
            newseg->wnd = seg.wnd;
            newseg->ts = current;
            newseg->sn = kcp->snd_nxt++;
            newseg->una = kcp->rcv_nxt;
            newseg->resendts = current;
            newseg->rto = (uint)kcp->rx_rto;
            newseg->fastack = 0;
            newseg->xmit = 0;
        }

        // calculate resent
        resent = kcp->fastresend > 0 ? (IUINT32)kcp->fastresend : 0xffffffff;
        rtomin = (uint)(kcp->nodelay == 0 ? kcp->rx_rto >> 3 : 0);

        // flush data segments
        for (p = kcp->snd_buf.next; p != &kcp->snd_buf; p = p->next)
        {
            IKCPSEG* segment = iqueue_entry(p);
            int needsend = 0;
            if (segment->xmit == 0)
            {
                needsend = 1;
                segment->xmit++;
                segment->rto = (uint)kcp->rx_rto;
                segment->resendts = current + segment->rto + rtomin;
            }
            else if (_itimediff(current, segment->resendts) >= 0)
            {
                needsend = 1;
                segment->xmit++;
                kcp->xmit++;
                if (kcp->nodelay == 0)
                {
                    segment->rto += _imax_(segment->rto, (IUINT32)kcp->rx_rto);
                }
                else
                {
                    IINT32 step = kcp->nodelay < 2 ?
                        (IINT32)segment->rto : kcp->rx_rto;
                    segment->rto += (uint)(step / 2);
                }
                segment->resendts = current + segment->rto;
                lost = 1;
            }
            else if (segment->fastack >= resent)
            {
                if ((int)segment->xmit <= kcp->fastlimit ||
                    kcp->fastlimit <= 0)
                {
                    needsend = 1;
                    segment->xmit++;
                    segment->fastack = 0;
                    segment->resendts = current + segment->rto;
                    change++;
                }
            }

            if (needsend != 0)
            {
                int need;
                segment->ts = current;
                segment->wnd = seg.wnd;
                segment->una = kcp->rcv_nxt;
                size = (int)(ptr - buffer);
                need = (int)(IKCP_OVERHEAD + segment->len);

                if (size + need > (int)kcp->mtu)
                {
                    ikcp_output(kcp, buffer, size);
                    ptr = buffer;
                }

                ptr = ikcp_encode_seg(ptr, segment);

                if (segment->len > 0)
                {
                    memcpy(ptr, segment->data, (int)segment->len);
                    ptr += segment->len;
                }

                if (segment->xmit >= kcp->dead_link)
                {
                    kcp->state = unchecked((IUINT32)(-1));
                }
            }
        }

        // flash remain segments
        size = (int)(ptr - buffer);
        if (size > 0)
        {
            ikcp_output(kcp, buffer, size);
        }

        // update ssthresh
        if (change != 0)
        {
            IUINT32 inflight = kcp->snd_nxt - kcp->snd_una;
            kcp->ssthresh = inflight / 2;
            if (kcp->ssthresh < IKCP_THRESH_MIN)
                kcp->ssthresh = IKCP_THRESH_MIN;
            kcp->cwnd = kcp->ssthresh + resent;
            kcp->incr = kcp->cwnd * kcp->mss;
        }

        if (lost != 0)
        {
            kcp->ssthresh = cwnd / 2;
            if (kcp->ssthresh < IKCP_THRESH_MIN)
                kcp->ssthresh = IKCP_THRESH_MIN;
            kcp->cwnd = 1;
            kcp->incr = kcp->mss;
        }

        if (kcp->cwnd < 1)
        {
            kcp->cwnd = 1;
            kcp->incr = kcp->mss;
        }
    }


    //---------------------------------------------------------------------
    // update state (call it repeatedly, every 10ms-100ms), or you can ask 
    // ikcp_check when to call it again (without ikcp_input/_send calling).
    // 'current' - current timestamp in millisec. 
    //---------------------------------------------------------------------
    public static void ikcp_update(ikcpcb* kcp, IUINT32 current)
    {
        IINT32 slap;

        kcp->current = current;

        if (kcp->updated == 0)
        {
            kcp->updated = 1;
            kcp->ts_flush = kcp->current;
        }

        slap = (int)_itimediff(kcp->current, kcp->ts_flush);

        if (slap >= 10000 || slap < -10000)
        {
            kcp->ts_flush = kcp->current;
            slap = 0;
        }

        if (slap >= 0)
        {
            kcp->ts_flush += kcp->interval;
            if (_itimediff(kcp->current, kcp->ts_flush) >= 0)
            {
                kcp->ts_flush = kcp->current + kcp->interval;
            }
            ikcp_flush(kcp);
        }
    }


    //---------------------------------------------------------------------
    // Determine when should you invoke ikcp_update:
    // returns when you should invoke ikcp_update in millisec, if there 
    // is no ikcp_input/_send calling. you can call ikcp_update in that
    // time, instead of call update repeatly.
    // Important to reduce unnacessary ikcp_update invoking. use it to 
    // schedule ikcp_update (eg. implementing an epoll-like mechanism, 
    // or optimize ikcp_update when handling massive kcp connections)
    //---------------------------------------------------------------------
    public static IUINT32 ikcp_check(ikcpcb* kcp, IUINT32 current)
    {
        IUINT32 ts_flush = kcp->ts_flush;
        IINT32 tm_flush = 0x7fffffff;
        IINT32 tm_packet = 0x7fffffff;
        IUINT32 minimal = 0;

        IQUEUEHEAD* p;

        if (kcp->updated == 0)
        {
            return current;
        }

        if (_itimediff(current, ts_flush) >= 10000 ||
            _itimediff(current, ts_flush) < -10000)
        {
            ts_flush = current;
        }
        if (_itimediff(current, ts_flush) >= 0)
        {
            return current;
        }

        tm_flush = (int)_itimediff(ts_flush, current);

        for (p = kcp->snd_buf.next; p != &kcp->snd_buf; p = p->next)
        {
            IKCPSEG* seg = iqueue_entry(p);
            IINT32 diff = (int)_itimediff(seg->resendts, current);
            if (diff <= 0)
            {
                return current;
            }
            if (diff < tm_packet) tm_packet = diff;
        }

        minimal = (IUINT32)(tm_packet < tm_flush ? tm_packet : tm_flush);
        if (minimal >= kcp->interval) minimal = kcp->interval;

        return current + minimal;
    }

    public static int ikcp_setmtu(ikcpcb* kcp, int mtu)
    {
        byte* buffer;
        if (mtu < 50 || mtu < (int)IKCP_OVERHEAD)
            return -1;
        buffer = (byte*)ikcp_malloc((size_t)((mtu + IKCP_OVERHEAD) * 3));
        if (buffer == null)
            return -2;
        kcp->mtu = (uint)mtu;
        kcp->mss = kcp->mtu - IKCP_OVERHEAD;
        ikcp_free(kcp->buffer);
        kcp->buffer = buffer;
        return 0;
    }

    static int ikcp_interval(ikcpcb* kcp, int interval)
    {
        if (interval > 5000) interval = 5000;
        else if (interval < 10) interval = 10;
        kcp->interval = (uint)interval;
        return 0;
    }

    public static int ikcp_nodelay(ikcpcb* kcp, int nodelay, int interval, int resend, int nc)
    {
        if (nodelay >= 0)
        {
            kcp->nodelay = (uint)nodelay;
            if (nodelay != 0)
            {
                kcp->rx_minrto = (int)IKCP_RTO_NDL;
            }
            else
            {
                kcp->rx_minrto = (int)IKCP_RTO_MIN;
            }
        }
        if (interval >= 0)
        {
            if (interval > 5000) interval = 5000;
            else if (interval < 10) interval = 10;
            kcp->interval = (uint)interval;
        }
        if (resend >= 0)
        {
            kcp->fastresend = resend;
        }
        if (nc >= 0)
        {
            kcp->nocwnd = nc;
        }
        return 0;
    }


    public static int ikcp_wndsize(ikcpcb* kcp, int sndwnd, int rcvwnd)
    {
        if (kcp != null)
        {
            if (sndwnd > 0)
            {
                kcp->snd_wnd = (uint)sndwnd;
            }
            if (rcvwnd > 0)
            {   // must >= max fragment size
                kcp->rcv_wnd = _imax_((uint)rcvwnd, IKCP_WND_RCV);
            }
        }
        return 0;
    }

    public static int ikcp_waitsnd(ikcpcb* kcp)
    {
        return (int)(kcp->nsnd_buf + kcp->nsnd_que);
    }


    // read conv
    public static IUINT32 ikcp_getconv(void* ptr)
    {
        IUINT32 conv;
        ikcp_decode32u((byte*)ptr, &conv);
        return conv;
    }
}
