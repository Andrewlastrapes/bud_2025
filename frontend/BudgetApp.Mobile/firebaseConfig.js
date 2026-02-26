import { initializeApp } from 'firebase/app';
import {
  getAuth,
  initializeAuth,
  getReactNativePersistence,
} from 'firebase/auth';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { Platform } from 'react-native';


const firebaseConfig = {
  apiKey: "AIzaSyBJCdNDCUNP628NHfms3tBd4aCRW3xGuWo",
  authDomain: "budget-97a7c.firebaseapp.com",
  projectId: "budget-97a7c",
  storageBucket: "budget-97a7c.firebasestorage.app",
  messagingSenderId: "1007494246029",
  appId: "1:1007494246029:web:d38015ec448cb8d9c43cc4",
  measurementId: "G-K12RHVFC7S"
};

const app = initializeApp(firebaseConfig);

let auth;

if (Platform.OS === 'web') {
  auth = getAuth(app);
} else {
  auth = initializeAuth(app, {
    persistence: getReactNativePersistence(AsyncStorage),
  });
}

export { app, auth };
