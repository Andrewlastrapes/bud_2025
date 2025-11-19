import React, { useState, useEffect } from 'react';
import { View, StyleSheet, FlatList } from 'react-native';
import { Text, Button, List, TextInput, Modal, Portal, Card, IconButton, ActivityIndicator } from 'react-native-paper';
import { SafeAreaView } from 'react-native-safe-area-context';
import axios from 'axios';
import { auth } from '../firebaseConfig';
import { useIsFocused } from '@react-navigation/native';

const API_BASE_URL = 'http://localhost:5150';

export default function FixedCostsScreen() {
    const [costs, setCosts] = useState([]);
    const [isLoading, setIsLoading] = useState(false);
    const [isModalVisible, setIsModalVisible] = useState(false);
    const [newName, setNewName] = useState('');
    const [newAmount, setNewAmount] = useState('');
    const isFocused = useIsFocused();

    const getAuthHeader = async () => {
        const user = auth.currentUser;
        if (!user) throw new Error("No user logged in.");
        const token = await user.getIdToken();
        return { headers: { 'Authorization': `Bearer ${token}` } };
    };

    const fetchCosts = async () => {
        setIsLoading(true);
        try {
            const config = await getAuthHeader();
            const response = await axios.get(`${API_BASE_URL}/api/fixed-costs`, config);
            setCosts(response.data);
        } catch (e) { console.error("Failed to fetch costs:", e); }
        setIsLoading(false);
    };

    useEffect(() => {
        if (isFocused) fetchCosts();
    }, [isFocused]);

    const handleAddCost = async () => {
        try {
            const config = await getAuthHeader();
            const amount = parseFloat(newAmount);
            await axios.post(`${API_BASE_URL}/api/fixed-costs`, { name: newName, amount }, config);

            setNewName('');
            setNewAmount('');
            setIsModalVisible(false);

            fetchCosts();
        } catch (e) {
            console.error("Failed to add cost:", e);
            alert("Failed to add cost.");
        }
    };

    const handleDeleteCost = async (id) => {
        try {
            const config = await getAuthHeader();
            await axios.delete(`${API_BASE_URL}/api/fixed-costs/${id}`, config);
            fetchCosts();
        } catch (e) {
            console.error("Failed to delete cost:", e);
        }
    };

    return (
        <SafeAreaView style={styles.container}>
            <Button
                mode="contained"
                onPress={() => setIsModalVisible(true)}
                style={styles.addButton}
            >
                Add Manual Cost
            </Button>

            {isLoading ? <ActivityIndicator /> : (
                <FlatList
                    data={costs}
                    keyExtractor={(item) => item.id.toString()}
                    // --- SYNTAX FIX WAS HERE ---
                    renderItem={({ item }) => (
                        <List.Item
                            title={item.name}
                            description={`$${item.amount.toFixed(2)}`}
                            left={() => <List.Icon icon={item.type === 'manual' ? 'account-edit' : 'bank-check'} />}
                            right={() => (
                                <IconButton
                                    icon="delete"
                                    iconColor="red"
                                    onPress={() => handleDeleteCost(item.id)}
                                />
                            )}
                        />
                    )}
                />
            )}

            {/* --- Add New Cost Modal --- */}
            <Portal>
                <Modal visible={isModalVisible} onDismiss={() => setIsModalVisible(false)} contentContainerStyle={styles.modal}>
                    <Card>
                        <Card.Title title="Add Manual Fixed Cost" />
                        <Card.Content>
                            <TextInput
                                label="Name (e.g., Rent, Venmo)"
                                value={newName}
                                onChangeText={setNewName}
                                style={{ marginBottom: 10 }}
                            />
                            <TextInput
                                label="Amount ($)"
                                value={newAmount}
                                onChangeText={setNewAmount}
                                keyboardType="numeric"
                                style={{ marginBottom: 15 }}
                            />
                            <Button mode="contained" onPress={handleAddCost}>Save</Button>
                        </Card.Content>
                    </Card>
                </Modal>
            </Portal>
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    container: { flex: 1 },
    addButton: { margin: 20 },
    modal: { padding: 20 },
});