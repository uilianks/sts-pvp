import java.lang.reflect.Constructor;
import java.lang.reflect.Method;
import java.util.Queue;

/** Run on the game's JRE with -Xverify:all, mod jar ahead of desktop-1.0.jar. */
public class VerifyAndNetTest {
    public static void main(String[] a) throws Exception {
        ClassLoader cl = VerifyAndNetTest.class.getClassLoader();
        String[] classes = {"com.megacrit.cardcrawl.helpers.MonsterHelper", "pvp.Pvp", "pvp.Net",
                "pvp.OpponentMonster", "pvp.WaitForOpponentAction"};
        for (String n : classes) {
            try {
                Class<?> c = Class.forName(n, true, cl);
                System.out.println("verified " + n + " from " + c.getProtectionDomain().getCodeSource().getLocation());
            } catch (VerifyError e) {
                throw e;
            } catch (Throwable t) { // static init needing a running game is fine: verification already passed
                System.out.println("verified " + n + " (init: " + t.getClass().getSimpleName() + ")");
            }
        }
        // Net over loopback: host + client in one JVM.
        Class<?> net = Class.forName("pvp.Net");
        Constructor<?> ctor = net.getDeclaredConstructor(String.class, int.class);
        ctor.setAccessible(true);
        Object host = ctor.newInstance(null, 24999);
        Thread.sleep(300);
        Object client = ctor.newInstance("127.0.0.1", 24999);
        Method send = net.getDeclaredMethod("send", String.class); send.setAccessible(true);
        Method connected = net.getDeclaredMethod("connected"); connected.setAccessible(true);
        for (int i = 0; i < 50 && !((Boolean) connected.invoke(host) && (Boolean) connected.invoke(client)); i++) Thread.sleep(100);
        send.invoke(client, "HIT 6 NORMAL");
        send.invoke(host, "SYNC 1");
        java.lang.reflect.Field f = net.getDeclaredField("inbox"); f.setAccessible(true);
        Queue<?> hin = (Queue<?>) f.get(host), cin = (Queue<?>) f.get(client);
        for (int i = 0; i < 50 && (hin.isEmpty() || cin.isEmpty()); i++) Thread.sleep(100);
        if (!"HIT 6 NORMAL".equals(hin.poll()) || !"SYNC 1".equals(cin.poll())) throw new AssertionError("net exchange failed");
        System.out.println("net exchange ok");
    }
}
