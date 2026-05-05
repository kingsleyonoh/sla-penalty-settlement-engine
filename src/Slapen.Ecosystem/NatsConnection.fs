namespace Slapen.Ecosystem

open System
open System.IO
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Tasks

type NatsConnectionConfig =
    { Url: string
      CredentialsPath: string option
      StreamName: string
      DurableName: string
      Enabled: bool }

type NatsConnection =
    private { Config: NatsConnectionConfig }

[<RequireQualifiedAccess>]
module NatsConnection =
    type ConnectionResult =
        | Disabled
        | Connected

    let create config = { Config = config }

    let private endpoint url =
        let uri = Uri url
        uri.Host, uri.Port

    let private write (writer: StreamWriter) (value: string) =
        task {
            do! writer.WriteAsync value
            do! writer.FlushAsync()
        }

    let private readLineWithin (reader: StreamReader) (timeout: TimeSpan) =
        task {
            use cancellation = new CancellationTokenSource(timeout)

            try
                return! reader.ReadLineAsync(cancellation.Token).AsTask()
            with :? OperationCanceledException ->
                return null
        }

    let private readCharsWithin (reader: StreamReader) (buffer: char array) offset count (timeout: TimeSpan) =
        task {
            use cancellation = new CancellationTokenSource(timeout)

            try
                return! reader.ReadAsync(buffer.AsMemory(offset, count), cancellation.Token).AsTask()
            with :? OperationCanceledException ->
                return 0
        }

    let private connectSocket connection =
        task {
            let host, port = endpoint connection.Config.Url
            let client = new TcpClient()
            do! client.ConnectAsync(host, port)
            let stream = client.GetStream()
            let encoding = UTF8Encoding(false)
            let reader = new StreamReader(stream, encoding)
            let writer = new StreamWriter(stream, encoding, leaveOpen = true)
            writer.NewLine <- "\r\n"
            let! _info = readLineWithin reader (TimeSpan.FromSeconds 5.0)
            do! write writer "CONNECT {\"verbose\":false,\"pedantic\":false}\r\nPING\r\n"
            let mutable line = ""

            while line <> "PONG" do
                let! read = readLineWithin reader (TimeSpan.FromSeconds 5.0)

                if isNull read then
                    failwith "NATS connection timed out waiting for PONG."

                line <- read

            return client, reader, writer
        }

    let connect connection : Task<ConnectionResult> =
        task {
            if not connection.Config.Enabled then
                return Disabled
            else
                let! client, reader, writer = connectSocket connection
                use _client = client
                use _reader = reader
                use _writer = writer
                return Connected
        }

    let publish (connection: NatsConnection) (subject: string) (payload: string) : Task =
        task {
            if connection.Config.Enabled then
                let! client, reader, writer = connectSocket connection
                use _client = client
                use _reader = reader
                use _writer = writer
                let bytes = Encoding.UTF8.GetByteCount payload
                do! write writer $"PUB {subject} {bytes}\r\n{payload}\r\n"
            else
                ()
        }

    let consumeOnce (connection: NatsConnection) (subject: string) (timeout: TimeSpan) : Task<string option> =
        task {
            if not connection.Config.Enabled then
                return None
            else
                let! client, reader, writer = connectSocket connection
                use _client = client
                use _reader = reader
                use _writer = writer
                client.ReceiveTimeout <- int timeout.TotalMilliseconds
                do! write writer $"SUB {subject} _INBOX.SLAPEN 1\r\n"
                let deadline = DateTimeOffset.UtcNow.Add timeout
                let mutable found = None

                while Option.isNone found && DateTimeOffset.UtcNow < deadline do
                    let remaining = deadline - DateTimeOffset.UtcNow

                    let readTimeout =
                        if remaining > TimeSpan.Zero then
                            remaining
                        else
                            TimeSpan.FromMilliseconds 1.0

                    let! line = readLineWithin reader readTimeout

                    if not (isNull line) && line.StartsWith("MSG ", StringComparison.Ordinal) then
                        let parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        let length = Int32.Parse(parts[parts.Length - 1])
                        let buffer = Array.zeroCreate<char> length
                        let mutable read = 0

                        while read < length do
                            let! count = readCharsWithin reader buffer read (length - read) readTimeout

                            if count = 0 then
                                failwith "NATS message body timed out before the declared payload length was read."

                            read <- read + count

                        let! _ = readLineWithin reader readTimeout
                        found <- Some(String buffer)
                    elif isNull line then
                        found <- None

                return found
        }
