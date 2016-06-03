namespace SharpRUDP
{
    public enum ConnectionState
    {
        /// <summary>
        ///     正在打开
        /// </summary>
        OPENING,

        /// <summary>
        ///     打开
        /// </summary>
        OPEN,

        /// <summary>
        ///     监听
        /// </summary>
        LISTEN,

        /// <summary>
        ///     正在关闭
        /// </summary>
        CLOSING,

        /// <summary>
        ///     关闭
        /// </summary>
        CLOSED
    }
}