import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

// Release signing: android/keystore.properties (gitignored) or ORBIT_KEYSTORE* environment variables
// (CI). See docs/RELEASING.md. Without either, release builds fall back to the debug key and print a
// warning: fine for local testing, never for publishing.
val signing = Properties().apply {
    val f = rootProject.file("keystore.properties")
    if (f.exists()) f.inputStream().use { load(it) }
}
fun signingValue(key: String, env: String): String? = signing.getProperty(key) ?: System.getenv(env)
val releaseStore = signingValue("storeFile", "ORBIT_KEYSTORE")

android {
    namespace = "io.github.mohib76.orbit"
    compileSdk = 35

    defaultConfig {
        applicationId = "io.github.mohib76.orbit"
        minSdk = 29
        targetSdk = 35
        versionCode = 4
        versionName = "1.3.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    signingConfigs {
        if (releaseStore != null) create("release") {
            storeFile = file(releaseStore)
            storePassword = signingValue("storePassword", "ORBIT_KEYSTORE_PASSWORD")
            keyAlias = signingValue("keyAlias", "ORBIT_KEY_ALIAS")
            keyPassword = signingValue("keyPassword", "ORBIT_KEY_PASSWORD")
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfig = if (releaseStore != null) signingConfigs.getByName("release") else {
                logger.warn("Orbit: no release keystore configured; signing the release APK with the DEBUG key.")
                signingConfigs.getByName("debug")
            }
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures {
        compose = true
        buildConfig = true
    }
    packaging {
        resources.excludes += setOf("META-INF/versions/9/OSGI-INF/MANIFEST.MF", "META-INF/*.kotlin_module")
    }
}

// Bundle the license and third-party notices from the repository root, for the About screen.
val noticesDir = layout.buildDirectory.dir("generated/notices")
val copyNotices by tasks.registering(Copy::class) {
    from(rootProject.file("../LICENSE"), rootProject.file("../THIRD_PARTY_NOTICES.md"))
    into(noticesDir)
}
android.sourceSets["main"].assets.srcDir(noticesDir)
tasks.named("preBuild") { dependsOn(copyNotices) }

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2024.12.01")
    implementation(composeBom)
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.activity:activity-compose:1.9.3")
    // Play services pulls an old Fragment that breaks registerForActivityResult; pin a current one.
    implementation("androidx.fragment:fragment-ktx:1.8.5")
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")

    // Self-signed TLS certificate for this phone's identity.
    implementation("org.bouncycastle:bcpkix-jdk18on:1.79")
    // QR scanning through Google Play services (no camera permission needed).
    implementation("com.google.android.gms:play-services-code-scanner:16.1.0")

    testImplementation("junit:junit:4.13.2")
    // Android's org.json is a stub on the JVM; use the real one in unit tests.
    testImplementation("org.json:json:20240303")
}
