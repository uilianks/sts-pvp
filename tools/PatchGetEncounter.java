import java.nio.file.Files;
import java.nio.file.Paths;

import jdk.internal.org.objectweb.asm.ClassReader;
import jdk.internal.org.objectweb.asm.ClassVisitor;
import jdk.internal.org.objectweb.asm.ClassWriter;
import jdk.internal.org.objectweb.asm.Label;
import jdk.internal.org.objectweb.asm.MethodVisitor;
import jdk.internal.org.objectweb.asm.Opcodes;

/**
 * Prepends to MonsterHelper.getEncounter(String):
 *
 *     MonsterGroup duel = pvp.Pvp.encounter();
 *     if (duel != null) return duel;
 *
 * Usage: PatchGetEncounter <in.class> <out.class>
 */
public final class PatchGetEncounter {
    private static final String NAME = "getEncounter";
    private static final String DESC = "(Ljava/lang/String;)Lcom/megacrit/cardcrawl/monsters/MonsterGroup;";

    public static void main(String[] args) throws Exception {
        ClassReader reader = new ClassReader(Files.readAllBytes(Paths.get(args[0])));
        ClassWriter writer = new ClassWriter(reader, 0);
        final int[] patched = {0};
        reader.accept(new ClassVisitor(Opcodes.ASM9, writer) {
            @Override
            public MethodVisitor visitMethod(int access, String name, String desc, String sig, String[] exc) {
                MethodVisitor mv = super.visitMethod(access, name, desc, sig, exc);
                if (!NAME.equals(name) || !DESC.equals(desc)) {
                    return mv;
                }
                patched[0]++;
                return new MethodVisitor(Opcodes.ASM9, mv) {
                    @Override
                    public void visitCode() {
                        super.visitCode();
                        Label original = new Label();
                        visitMethodInsn(Opcodes.INVOKESTATIC, "pvp/Pvp", "encounter",
                                "()Lcom/megacrit/cardcrawl/monsters/MonsterGroup;", false);
                        visitInsn(Opcodes.DUP);
                        visitJumpInsn(Opcodes.IFNULL, original);
                        visitInsn(Opcodes.ARETURN);
                        visitLabel(original);
                        // Stack holds the null left by DUP; frame: same locals, one stack item.
                        visitFrame(Opcodes.F_SAME1, 0, null, 1,
                                new Object[] {"com/megacrit/cardcrawl/monsters/MonsterGroup"});
                        visitInsn(Opcodes.POP);
                    }

                    @Override
                    public void visitMaxs(int maxStack, int maxLocals) {
                        super.visitMaxs(Math.max(maxStack, 2), maxLocals);
                    }
                };
            }
        }, 0);
        if (patched[0] != 1) {
            throw new IllegalStateException("expected exactly one " + NAME + DESC + ", found " + patched[0]);
        }
        Files.createDirectories(Paths.get(args[1]).toAbsolutePath().getParent());
        Files.write(Paths.get(args[1]), writer.toByteArray());
    }
}
