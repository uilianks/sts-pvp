package pvp;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.io.PrintWriter;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ConcurrentLinkedQueue;

/**
 * One TCP link to the other player, one text line per message. Pure Java, no game classes,
 * so it can be tested without the game. The host listens on every interface (Hamachi
 * included); the client retries until the host is up.
 */
final class Net {
    final ConcurrentLinkedQueue<String> inbox = new ConcurrentLinkedQueue<>();
    private volatile PrintWriter out;
    private volatile boolean closed;

    /** host == null: listen on port. Otherwise connect to host:port. */
    Net(final String host, final int port) {
        Thread t = new Thread(() -> run(host, port), "pvp-net");
        t.setDaemon(true);
        t.start();
    }

    boolean connected() {
        return out != null && !closed;
    }

    /** The link was up and is now gone. */
    boolean lost() {
        return closed;
    }

    void send(String line) {
        PrintWriter w = out;
        if (w != null && !closed) {
            synchronized (w) {
                w.println(line);
                w.flush();
            }
        }
    }

    private void run(String host, int port) {
        try (Socket socket = host == null ? accept(port) : connect(host, port)) {
            socket.setTcpNoDelay(true);
            BufferedReader in = new BufferedReader(
                    new InputStreamReader(socket.getInputStream(), StandardCharsets.UTF_8));
            out = new PrintWriter(new OutputStreamWriter(socket.getOutputStream(), StandardCharsets.UTF_8));
            Pvp.log("connected to " + socket.getRemoteSocketAddress());
            for (String line; (line = in.readLine()) != null; ) {
                inbox.add(line);
            }
            Pvp.log("opponent closed the connection");
        } catch (IOException e) {
            Pvp.log("connection error: " + e);
        } finally {
            closed = true;
        }
    }

    private static Socket accept(int port) throws IOException {
        try (ServerSocket server = new ServerSocket()) {
            server.setReuseAddress(true);
            server.bind(new InetSocketAddress(port));
            Pvp.log("hosting, waiting for opponent on port " + port);
            return server.accept();
        }
    }

    private static Socket connect(String host, int port) throws IOException {
        Pvp.log("joining " + host + ":" + port);
        while (true) {
            Socket s = new Socket();
            try {
                s.connect(new InetSocketAddress(host, port), 3000);
                return s;
            } catch (IOException e) {
                s.close();
                try {
                    Thread.sleep(1000);
                } catch (InterruptedException ie) {
                    throw new IOException(ie);
                }
            }
        }
    }
}
