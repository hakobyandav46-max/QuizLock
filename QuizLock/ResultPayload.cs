namespace QuizLock
{
    /// <summary>
    /// The JSON shape sent from a Quiz Station laptop to a Collector laptop
    /// over the local network.
    /// </summary>
    internal sealed class ResultPayload
    {
        public string? Name { get; set; }
        public string? Score { get; set; }
        public string? QuizUrl { get; set; }
        public string? Timestamp { get; set; }
    }
}
