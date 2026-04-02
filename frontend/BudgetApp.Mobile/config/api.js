import axios from 'axios';
import * as Sentry from '@sentry/react-native';

const API_BASE_URL = process.env.EXPO_PUBLIC_API_URL;

if (!API_BASE_URL) {
  throw new Error('EXPO_PUBLIC_API_URL is not defined');
}

// --- Axios response interceptor: capture failed API calls in Sentry ---
axios.interceptors.response.use(
  (response) => response,
  (error) => {
    // Skip 404s on the user-profile retry loop — those are expected
    const status = error?.response?.status;
    const url = error?.config?.url ?? 'unknown';
    const method = (error?.config?.method ?? 'unknown').toUpperCase();

    if (status !== 404) {
      Sentry.withScope((scope) => {
        scope.setTag('api.method', method);
        scope.setTag('api.status', String(status ?? 'network_error'));
        scope.setContext('api_request', {
          url,
          method,
          status: status ?? null,
          message: error?.message,
        });
        Sentry.captureException(error);
      });
    }

    return Promise.reject(error);
  },
);

export { API_BASE_URL };