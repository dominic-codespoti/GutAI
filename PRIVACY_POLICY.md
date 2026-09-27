# GutLens Privacy Policy

**Effective Date:** February 24, 2026  
**Last Updated:** September 25, 2026

GutLens ("we", "us", "our") is a gut-health food diary app that helps you track meals, monitor symptoms, and discover food-related patterns. GutLens is developed as the GutAI project. This privacy policy explains what data we collect, how we use it, and your rights.

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
- Meal drafts created from photos, Coach or connected AI assistant suggestions, text logging, or meal suggestions, including your edits and corrections

### Symptom Logs

- Symptom type, severity, timing, and duration
- Free-text notes about symptoms
- Associations between meals and symptoms

### Derived Data

- Food-symptom correlations and trigger food identification
- Additive exposure tracking
- Personalized gut-health insights

We generate personalized insights on our servers from your diary data and do not use them to train AI models. When you request meal suggestions, we send the screened candidate food list described below.

### Operational Telemetry

- For each photo scan, the AI usage summary log records the operation name, scan correlation/draft ID, stage names, model-call counts, deployment names, input and output token counts, model time, and estimated cost. This includes usage for photo analysis, candidate selection, agent review, and web-nutrition extraction. Operation-tagged metrics report token counts, model time, and estimated cost when available. Telemetry is sent to Application Insights; its Log Analytics workspace is configured for 30-day retention. Estimated cost can be unavailable when deployment pricing is not configured.

---

## 2. Data We Do NOT Collect

- Location or GPS data
- Device identifiers or advertising IDs
- Contacts, call logs, or messages
- Advertising or behavioral tracking data
- Push notification tokens — optional meal reminders are scheduled locally on your device and are never sent through our servers
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
| Generate meal suggestions when you request them          | Remaining nutrition budget, requested preferences, and a screened candidate food list |
| Monitor AI service operation and usage costs | Scan identifier, workload stage and model deployment, token counts, model time, estimated cost |

We do **not** use your data for advertising, profiling, or sale to third parties.

---

## 4. Third-Party Services

When you search for or log a food, we may query external nutrition databases to retrieve nutritional data. These services receive **only** the food search text or barcode — never your email, name, health data, or any personal identifiers.

| Service                                             | Data Sent                                      | Purpose                           |
| --------------------------------------------------- | ---------------------------------------------- | --------------------------------- |
| [USDA FoodData Central](https://fdc.nal.usda.gov/)  | Food name                                      | Nutrition lookup                  |
| [Open Food Facts](https://world.openfoodfacts.org/) | Food name or barcode | Nutrition lookup and barcode-based food lookup |
| DuckDuckGo and Jina Reader | Food-name nutrition search query to DuckDuckGo; selected result URL to Jina Reader | Optional web nutrition lookup, disabled by default pending review |
| Microsoft Azure OpenAI | Meal descriptions, Coach chat messages, meal photos you submit, meal-suggestion requests, and (if web grounding is enabled) selected web-page content are processed solely to generate results such as parsing, coaching replies, photo analysis, and meal suggestions | AI features |

When `Features:WebGrounding` is enabled, web nutrition results are cached in Azure Table Storage using the normalized food name and region, not your user ID, and are not linked to your account. Successful results are reused for up to 180 days; not-found results for up to 7 days. Expired entries are not automatically purged; they remain stored unless a later lookup overwrites them.
For meal suggestions, the request includes your remaining nutrition budget, any preferences you enter for that request, and a candidate list containing food names and nutrition/serving information. The candidate list is built from foods that are recent or considered safe and is screened against your saved allergies and dietary preferences; those saved lists are not sent as a separate list. If you add a note to a meal photo or request a food description, that note or description is sent with the request for AI processing.
Apart from services listed here, AI providers operating connected assistants, and the optional health-platform sync described below, no other third-party services receive your data.

### Optional AI Assistant Connections

If you choose to connect an external AI assistant (e.g., a chat app that supports MCP), GutLens issues you a one-time pairing code to link it. Once linked:

- A personal access token (PAT) without write scope is read-only; a PAT with write scope can use the available write tools.
- Meals proposed by a connected assistant appear as drafts. They are logged only after you review and confirm them in GutLens or in the assistant chat; they are never logged automatically.
- Your data passes through the AI provider that operates that assistant; their handling of that data is governed by their own privacy policy.
- You can see every connected assistant and revoke its access instantly at any time (Settings → Connected AI Assistants). Revocation takes effect immediately.
- Access tokens are stored only as cryptographic hashes; they can never be viewed again after creation.

### Optional Health Platform Sync

GutLens can optionally exchange meal data with Apple Health (iOS) and Google Health Connect (Android), with your permission:

- **Import:** when you request an import, GutLens reads nutrition records from your device's health store and sends selected meal details to GutLens's API to create imported meal entries. Imported entries are labeled as estimates and never treated as verified data.
- **Export:** you can choose to have meals you log in GutLens written to your device's health store. This toggle is off by default.
- Health data is exchanged between the selected platform, your device, and GutLens. GutLens does not send health-sync records to other third-party services. You can revoke permission in iOS/Android system settings.
- When importing, GutLens skips records it originally wrote itself, so enabling both directions never duplicates your meals.

---

## 5. Data Storage & Security

- All data is stored on secured servers hosted on Microsoft Azure
- Passwords are hashed using industry-standard algorithms before storage
- Authentication uses short-lived JSON Web Tokens (JWTs) stored securely on your device (Expo SecureStore on mobile)
- API communication is encrypted via HTTPS/TLS
- IP addresses are used transiently for rate limiting and are not persisted

---

## 6. Your Rights

You have control over your data:

- **Access & Export** — Export your meal and health data through the app.
- **Correction** — Update your profile, preferences, and logged entries.
- **Deletion** — Delete your account in Settings. This is irreversible: account deletion removes your profile and identity, related credentials and alerts, and draft records; meal and symptom records are marked as deleted.
- **Portability** — Your exported data is provided in a standard format you can take elsewhere.

If you are in the EU/EEA, you also have rights under the GDPR including the right to restrict processing and the right to object. Contact us to exercise these rights.

---

## 7. Data Retention

- Your account profile and identity are retained while your account is active.
- AI meal drafts from photos, Coach or connected AI assistant suggestions, text logging, and meal suggestions remain pending until you review them. Pending drafts expire after 24 hours if not reviewed.
- Reviewed or otherwise closed drafts, including discarded drafts and your corrections used to improve portion accuracy, are retained for up to 90 days from creation and then purged.
- Drafts and their correction data are deleted when you delete your account. Account deletion also removes related credentials and alerts and marks meal and symptom records as deleted.

---

## 8. Children's Privacy

GutLens is not directed at children under 13 (or 16 in the EU/EEA). We do not knowingly collect data from children. If you believe a child has provided us with personal data, please contact us and we will delete it.

---

## 9. Changes to This Policy

We may update this privacy policy from time to time. Changes will be reflected by updating the "Last Updated" date at the top. Continued use of the app after changes constitutes acceptance.

---

## 10. Contact

If you have questions about this privacy policy or your data, contact us at:

**Email:** domcodespoti@gmail.com

---

_This privacy policy is also available at: [https://github.com/dominic-codespoti/GutAI/blob/main/PRIVACY_POLICY.md](https://github.com/dominic-codespoti/GutAI/blob/main/PRIVACY_POLICY.md)_
