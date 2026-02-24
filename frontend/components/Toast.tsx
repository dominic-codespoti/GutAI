import { useEffect, useRef } from 'react';
import { View, Text, Animated, TouchableOpacity, Platform } from 'react-native';
import { useToastStore, type ToastType } from '../src/stores/toast';

const COLORS: Record<ToastType, { bg: string; border: string; text: string; icon: string }> = {
  error:   { bg: '#fef2f2', border: '#fca5a5', text: '#991b1b', icon: '✕' },
  success: { bg: '#f0fdf4', border: '#86efac', text: '#166534', icon: '✓' },
  info:    { bg: '#eff6ff', border: '#93c5fd', text: '#1e40af', icon: 'ℹ' },
};

function ToastItem({ id, message, type }: { id: number; message: string; type: ToastType }) {
  const dismiss = useToastStore((s) => s.dismiss);
  const opacity = useRef(new Animated.Value(0)).current;
  const translateY = useRef(new Animated.Value(-20)).current;
  const c = COLORS[type];

  useEffect(() => {
    Animated.parallel([
      Animated.timing(opacity, { toValue: 1, duration: 200, useNativeDriver: true }),
      Animated.timing(translateY, { toValue: 0, duration: 200, useNativeDriver: true }),
    ]).start();

    const timer = setTimeout(() => {
      Animated.timing(opacity, { toValue: 0, duration: 300, useNativeDriver: true }).start();
    }, 3500);
    return () => clearTimeout(timer);
  }, []);

  return (
    <Animated.View style={{ opacity, transform: [{ translateY }], marginBottom: 8 }}>
      <TouchableOpacity
        activeOpacity={0.8}
        onPress={() => dismiss(id)}
        style={{
          flexDirection: 'row',
          alignItems: 'center',
          backgroundColor: c.bg,
          borderWidth: 1,
          borderColor: c.border,
          borderRadius: 12,
          paddingHorizontal: 16,
          paddingVertical: 12,
          ...(Platform.OS === 'web' ? { boxShadow: '0 4px 12px rgba(0,0,0,0.08)' } as any : {
            shadowColor: '#000',
            shadowOffset: { width: 0, height: 2 },
            shadowOpacity: 0.08,
            shadowRadius: 8,
            elevation: 4,
          }),
        }}
      >
        <Text style={{ fontSize: 16, marginRight: 10 }}>{c.icon}</Text>
        <Text style={{ flex: 1, fontSize: 14, color: c.text, fontWeight: '500' }}>{message}</Text>
        <Text style={{ fontSize: 18, color: c.text, paddingLeft: 8, opacity: 0.5 }}>×</Text>
      </TouchableOpacity>
    </Animated.View>
  );
}

export default function ToastContainer() {
  const toasts = useToastStore((s) => s.toasts);

  if (toasts.length === 0) return null;

  return (
    <View
      pointerEvents="box-none"
      style={{
        position: 'absolute',
        top: Platform.OS === 'web' ? 16 : 56,
        left: 16,
        right: 16,
        zIndex: 9999,
      }}
    >
      {toasts.map((t) => (
        <ToastItem key={t.id} {...t} />
      ))}
    </View>
  );
}
