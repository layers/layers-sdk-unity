namespace Layers.Unity.Internal
{
    /// <summary>
    /// The Rust core's flush surface, behind an interface.
    ///
    /// In production this is <see cref="NativeFlushCore"/> and every method is a
    /// straight P/Invoke into the native library. The seam exists because the
    /// contract <see cref="FlushManager"/> owes the core is expressed entirely in
    /// the CALLS it makes — an aborted flush must return its drained batch to the
    /// Rust queue and release the circuit breaker's half-open probe claim
    /// (adr/0001-rust-owned-delivery-policy.md). Neither is observable from the
    /// outside: nothing throws, nothing logs, the events are simply gone and the
    /// breaker stays claimed until its 45 s expiry.
    ///
    /// A test run has no native library to call — that is exactly why
    /// <see cref="LayersTestMode"/> leaves <c>_flushManager</c> null — so without
    /// a seam those two obligations cannot be asserted at all.
    ///
    /// The native implementation lives here rather than in its own file because
    /// it carries no logic: it is the P/Invoke passthrough, and reading it beside
    /// the interface is how you check that.
    /// </summary>
    internal interface IFlushCore
    {
        /// <summary>
        /// Number of events waiting in the Rust queue. Negative if the SDK is
        /// not initialized.
        /// </summary>
        int QueueDepth();

        /// <summary>
        /// The single pre-flight delivery gate (consent/DNT + server Retry-After
        /// + circuit breaker). May CLAIM the breaker's half-open probe slot, so a
        /// <c>true</c> must always be followed by exactly one of
        /// <see cref="RecordFlushResult"/> or <see cref="AbortFlushAttempt"/>.
        /// </summary>
        bool ShouldAttemptFlush();

        /// <summary>The ingest URL for event batches, or null/empty if unavailable.</summary>
        string EventsUrl();

        /// <summary>Flush headers as JSON (array-of-pairs or object).</summary>
        string FlushHeadersJson();

        /// <summary>
        /// Remove up to <paramref name="count"/> events from the Rust queue as a
        /// serialized batch. Null/empty when the queue is empty. Once this
        /// returns, the returned string is the ONLY copy of those events.
        /// </summary>
        string DrainBatch(uint count);

        /// <summary>
        /// Put a drained batch back at the FRONT of the Rust queue. Returns the
        /// re-enqueued event count as a string on success, or an error message.
        /// </summary>
        string RequeueEvents(string batchJson);

        /// <summary>
        /// Report the outcome of an attempt that REACHED THE WIRE (status 0 =
        /// no response). Returns the verdict for the in-flight batch:
        /// 1 = Delivered, 2 = RetryLater (requeue), 3 = Drop (discard).
        /// </summary>
        byte RecordFlushResult(ushort status, string retryAfterHeader);

        /// <summary>
        /// Release a claimed half-open breaker probe WITHOUT recording a delivery
        /// outcome. For attempts that produced no evidence about the server:
        /// the queue raced empty, URL/header setup failed, the coroutine was
        /// cancelled.
        /// </summary>
        void AbortFlushAttempt();

        /// <summary>
        /// Crash-safety disk snapshot of the queue (the core's <c>layers_flush</c>;
        /// it does not deliver). Returns null on success, an error string otherwise.
        /// </summary>
        string FlushToDisk();
    }

    /// <summary>
    /// Production <see cref="IFlushCore"/>: P/Invoke straight through to the Rust
    /// core via <see cref="NativeBindings"/>. No logic, no state.
    /// </summary>
    internal sealed class NativeFlushCore : IFlushCore
    {
        public int QueueDepth() => NativeBindings.layers_queue_depth();

        public bool ShouldAttemptFlush() => NativeBindings.layers_should_attempt_flush() != 0;

        public string EventsUrl() =>
            NativeStringHelper.ReadAndFree(NativeBindings.layers_events_url());

        public string FlushHeadersJson() =>
            NativeStringHelper.ReadAndFree(NativeBindings.layers_flush_headers_json());

        public string DrainBatch(uint count) =>
            NativeStringHelper.ReadAndFree(NativeBindings.layers_drain_batch(count));

        public string RequeueEvents(string batchJson) =>
            NativeStringHelper.ReadAndFree(NativeBindings.layers_requeue_events(batchJson));

        public byte RecordFlushResult(ushort status, string retryAfterHeader) =>
            NativeBindings.layers_record_flush_result(status, retryAfterHeader);

        public void AbortFlushAttempt() => NativeBindings.layers_abort_flush_attempt();

        public string FlushToDisk() =>
            NativeStringHelper.ProcessResult(NativeBindings.layers_flush());
    }
}
