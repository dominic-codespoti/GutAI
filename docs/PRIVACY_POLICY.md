# GutAI Privacy Policy

**Effective Date:** February 24, 2026
**Last Updated:** February 24, 2026

GutAI ("we", "us", "our") is a gut-health food diary app that helps you track meals, monitor symptoms, and discover food-related patterns. This privacy policy explains what data we collect, how we use it, and your rights.

---

## 1. Data We Collect

### Account Information

- Email address
- Display name
- Password (stored as a salted hash — we never store or see your plaintext password)
- Timezone

### Health & Dietary Preferences

- Self-reported allergies (e.g., peanuts, dairy, gluten)
- Dietary preferences (e.g., vegan, keto, low-FODMAP)
- Daily nutrition goals (calories, protein, carbs, fat, fiber)

### Food Diary

- Meal entries including food names, portion sizes, and nutritional values
- Timestamps of when meals were logged
- Free-text notes and natural language meal descriptions (e.g., "ate 2 eggs and toast")
- Optional photo URLs

### Symptom Logs

- Symptom type, severity, timing, and duration
- Free-text notes about symptoms
- Associations between meals and symptoms

### Derived Data

- Food-symptom correlations and trigger food identification
- Additive exposure tracking
- Personalized gut-health insights

We generate these insights locally from your own data to help you understand patterns. They are not shared externally.

---

## 2. Data We Do NOT Collect

- Location or GPS data
- Device identifiers or advertising IDs
- Contacts, call logs, or messages
- Analytics or behavioral tracking
- Push notification tokens (not yet implemented)
- Biometric data

---

## 3. How We Use Your Data

| Purpose                                                  | Data Used                               |
| -------------------------------------------------------- | --------------------------------------- |
| Provide the core food diary and symptom tracking service | Meals, symptoms, preferences            |
| Generate personalized insights and trigger food analysis | Meals, symptoms, correlations           |
| Look up nutritional information for foods you log        | Food names, barcodes, meal descriptions |
| Authenticate your account                                | Email, hashed password, JWT tokens      |
| Comply with your nutrition goals                         | Calorie/macro targets                   |

We do **not** use your data for advertising, profiling, or sale to third parties.

---

## 4. Third-Party Services

When you search for or log a food, we may query external nutrition databases to retrieve nutritional data. These services receive **only** the food search text or barcode — never your email, name, health data, or any personal identifiers.

| Service                                             | Data Sent                                      | Purpose                           |
| --------------------------------------------------- | ---------------------------------------------- | --------------------------------- |
| [Edamam](https://www.edamam.com/)                   | Food name or natural language meal description | Nutrition lookup and meal parsing |
| [USDA FoodData Central](https://fdc.nal.usda.gov/)  | Food name                                      | Nutrition lookup                  |
| [Open Food Facts](https://world.openfoodfacts.org/) | Barcode                                        | Barcode-based food lookup         |
| [CalorieNinjas](https://calorieninjas.com/)         | Natural language meal description              | Fallback nutrition parsing        |

No other third-party services receive your data.

---

## 5. Data Storage & Security

- All data is stored on secured servers hosted on Microsoft Azure
- Passwords are hashed using industry-standard algorithms before storage
- Authentication uses short-lived JSON Web Tokens (JWTs) stored securely on your device (Expo SecureStore on mobile)
- API communication is encrypted via HTTPS/TLS
- IP addresses are used transiently for rate limiting and are not persisted

---

## 6. Your Rights

You have full control over your data:

- **Access & Export** — Export all your meal and health data at any time via the app (Settings → Export Data)
- **Correction** — Update your profile, preferences, and logged entries at any time
- **Deletion** — Delete your account and all associated data permanently (Settings → Delete Account). This is irreversible and removes all meals, symptoms, insights, and account information
- **Portability** — Your exported data is provided in a standard format you can take elsewhere

If you are in the EU/EEA, you also have rights under the GDPR including the right to restrict processing and the right to object. Contact us to exercise these rights.

---

## 7. Data Retention

- Your data is retained for as long as your account is active
- When you delete your account, all data is permanently deleted immediately
- We do not retain backups of deleted account data beyond standard infrastructure backup windows (up to 30 days), after which it is purged

---

## 8. Children's Privacy

GutAI is not directed at children under 13 (or 16 in the EU/EEA). We do not knowingly collect data from children. If you believe a child has provided us with personal data, please contact us and we will delete it.

---

## 9. Changes to This Policy

We may update this privacy policy from time to time. Changes will be reflected by updating the "Last Updated" date at the top. Continued use of the app after changes constitutes acceptance.

---

## 10. Contact

If you have questions about this privacy policy or your data, contact us at:

**Email:** support@workoutquestapp.com

---

_This privacy policy is also available at: [https://github.com/dominic-codespoti/GutAI/blob/main/PRIVACY_POLICY.md](https://github.com/dominic-codespoti/GutAI/blob/main/PRIVACY_POLICY.md)_
