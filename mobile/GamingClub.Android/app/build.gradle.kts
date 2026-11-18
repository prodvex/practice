plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.gamingclub.client"
    compileSdk = 35
    defaultConfig {
        applicationId = "com.gamingclub.client"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0"
    }
    buildTypes {
        debug {
            // Emulator-to-host address. Physical device needs PC LAN IP and HTTPS/proxy.
            buildConfigField("String", "API_BASE_URL", "\"http://10.0.2.2:5080\"")
        }
        release {
            isMinifyEnabled = false
            buildConfigField("String", "API_BASE_URL", "\"https://example.invalid\"")
        }
    }
    buildFeatures { buildConfig = true }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
}
