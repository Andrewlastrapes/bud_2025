// File: services/notificationRegistration.js
// Standalone helper for registering push notification devices with retry logic.
// Handles race condition where device registration may arrive before backend user creation.

import axios from "axios";
import * as Notifications from "expo-notifications";
import * as Device from "expo-device";
import { Platform } from "react-native";
import { API_BASE_URL } from "../config/api";

// In-flight guard and cache to prevent duplicate concurrent calls
let inFlightCall = null;
let lastRegistered = { uid: null, token: null };

/**
 * Registers the current device for push notifications with retry logic.
 *
 * @param {Object} fbUser - Firebase user object with getIdToken() method
 * @returns {Promise<boolean>} - true if registration succeeded, false otherwise
 *
 * Retry strategy:
 * - Retries: 404 "User not found", network errors, 5xx server errors
 * - No retry: 401, 403, 400, permission denied
 * - Max 5 attempts with exponential backoff (500ms, 1s, 2s, 4s)
 * - Total max wait: ~7.5 seconds
 */
export async function ensureDeviceRegisteredForCurrentUser(fbUser) {
  if (!fbUser) {
    console.log("[DeviceReg] No Firebase user provided, skipping registration");
    return false;
  }

  if (Platform.OS === "web") {
    console.log("[DeviceReg] Skipping push notification registration on web");
    return false;
  }

  if (!Device.isDevice) {
    console.log(
      "[DeviceReg] Push notifications require a physical device/emulator",
    );
    return false;
  }

  // Get Expo push token
  let expoPushToken;
  try {
    const { status: existingStatus } =
      await Notifications.getPermissionsAsync();
    let finalStatus = existingStatus;

    if (existingStatus !== "granted") {
      const { status } = await Notifications.requestPermissionsAsync();
      finalStatus = status;
    }

    if (finalStatus !== "granted") {
      console.log("[DeviceReg] Notification permission not granted");
      return false;
    }

    const tokenData = await Notifications.getExpoPushTokenAsync();
    expoPushToken = tokenData.data;
  } catch (e) {
    console.error("[DeviceReg] Failed to get Expo push token:", e.message);
    return false;
  }

  if (!expoPushToken) {
    console.log("[DeviceReg] No Expo push token available");
    return false;
  }

  // Check cache: skip if already registered for this uid+token
  const firebaseUid = fbUser.uid;
  if (
    lastRegistered.uid === firebaseUid &&
    lastRegistered.token === expoPushToken
  ) {
    console.log(
      `[DeviceReg] Already registered for Firebase UID ${firebaseUid} with token ...${expoPushToken.slice(-6)}`,
    );
    return true;
  }

  // In-flight guard: prevent duplicate concurrent calls
  if (inFlightCall) {
    console.log("[DeviceReg] Registration already in flight, waiting...");
    return await inFlightCall;
  }

  // Create the registration promise
  const registrationPromise = (async () => {
    const maxAttempts = 5;
    const backoffSchedule = [0, 500, 1000, 2000, 4000]; // ms

    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
      try {
        // Get fresh Firebase ID token
        const idToken = await fbUser.getIdToken(true);

        const tokenSuffix = expoPushToken.slice(-6);
        console.log(
          `[DeviceReg] Attempt ${attempt}/${maxAttempts} | Firebase UID: ${firebaseUid} | Platform: ${Platform.OS} | Token: ...${tokenSuffix}`,
        );

        const response = await axios.post(
          `${API_BASE_URL}/api/notifications/register-device`,
          { expoPushToken, platform: Platform.OS },
          { headers: { Authorization: `Bearer ${idToken}` } },
        );

        // Success!
        console.log(
          `[DeviceReg] Success | Status: ${response.status} | Firebase UID: ${firebaseUid}`,
        );

        // Update cache
        lastRegistered = { uid: firebaseUid, token: expoPushToken };

        return true;
      } catch (error) {
        const status = error.response?.status;
        const errorMessage = error.response?.data?.error || error.message;

        console.log(
          `[DeviceReg] Attempt ${attempt}/${maxAttempts} failed | Status: ${status || "network error"} | Error: ${errorMessage}`,
        );

        // Determine if we should retry
        const shouldRetry = shouldRetryError(error, attempt, maxAttempts);

        if (!shouldRetry) {
          console.error(
            `[DeviceReg] Failed permanently | Status: ${status || "network error"} | Error: ${errorMessage}`,
          );
          return false;
        }

        // Wait before retry (except on last attempt)
        if (attempt < maxAttempts) {
          const delay = backoffSchedule[attempt];
          console.log(`[DeviceReg] Retrying in ${delay}ms...`);
          await new Promise((resolve) => setTimeout(resolve, delay));
        }
      }
    }

    console.error(
      `[DeviceReg] Failed after ${maxAttempts} attempts | Firebase UID: ${firebaseUid}`,
    );
    return false;
  })();

  // Store in-flight promise
  inFlightCall = registrationPromise;

  try {
    return await registrationPromise;
  } finally {
    // Clear in-flight guard
    inFlightCall = null;
  }
}

/**
 * Determines whether an error should trigger a retry.
 *
 * Retry these:
 * - 404 "User not found" (race condition)
 * - Network errors (ECONNREFUSED, timeout, etc.)
 * - 5xx server errors
 *
 * Do NOT retry these:
 * - 401 Unauthorized (bad token)
 * - 403 Forbidden (permissions issue)
 * - 400 Bad Request (invalid data)
 */
function shouldRetryError(error, attempt, maxAttempts) {
  // Don't retry if we've exhausted attempts
  if (attempt >= maxAttempts) {
    return false;
  }

  const status = error.response?.status;

  // Network errors (no response) - retry
  if (!status) {
    return true;
  }

  // 404 User not found - retry (race condition)
  if (status === 404) {
    return true;
  }

  // 5xx server errors - retry
  if (status >= 500 && status < 600) {
    return true;
  }

  // 401, 403, 400 - do not retry
  if (status === 401 || status === 403 || status === 400) {
    return false;
  }

  // Other 4xx errors - do not retry
  if (status >= 400 && status < 500) {
    return false;
  }

  // Default: retry
  return true;
}
