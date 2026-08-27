// Expo config plugin: adds the Android package-visibility <queries> entry that
// Health Connect availability checks require on API 30+ (package com.google.android.apps.healthdata).
// react-native-health-connect's own plugin covers permissions + rationale routing,
const { withAndroidManifest } = require("@expo/config-plugins");

const HEALTH_CONNECT_PACKAGE = "com.google.android.apps.healthdata";

const withHealthConnectQueries = (config) =>
  withAndroidManifest(config, (config) => {
    const manifest = config.modResults.manifest;
    const queries = manifest.queries ?? (manifest.queries = [{}]);

    const hasPackage = (queryEntries, name) =>
      queryEntries.some((q) => (q.package || []).some((p) => p.$["android:name"] === name));

    if (!hasPackage(queries, HEALTH_CONNECT_PACKAGE)) {
      queries[0].package = queries[0].package || [];
      queries[0].package.push({
        $: { "android:name": HEALTH_CONNECT_PACKAGE },
      });
    }

    return config;
  });

module.exports = withHealthConnectQueries;
