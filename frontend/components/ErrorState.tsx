import { View, Text, TouchableOpacity } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

export function ErrorState({
  message = 'Something went wrong',
  onRetry,
}: {
  message?: string;
  onRetry?: () => void;
}) {
  return (
    <View style={{ alignItems: 'center', paddingVertical: 40 }}>
      <Ionicons name="cloud-offline-outline" size={48} color="#ef4444" />
      <Text style={{ color: '#ef4444', marginTop: 12, fontSize: 16, fontWeight: '600' }}>
        {message}
      </Text>
      {onRetry && (
        <TouchableOpacity
          onPress={onRetry}
          style={{
            marginTop: 12,
            backgroundColor: '#22c55e',
            paddingHorizontal: 24,
            paddingVertical: 10,
            borderRadius: 8,
          }}
        >
          <Text style={{ color: '#fff', fontWeight: '600' }}>Retry</Text>
        </TouchableOpacity>
      )}
    </View>
  );
}
