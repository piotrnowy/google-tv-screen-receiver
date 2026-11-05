import org.gradle.process.ExecOperations
import javax.inject.Inject

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

/** Compile the fixed CEC shell entry point independently of the large receiver/WebRTC APK. */
abstract class DexProjectorPower : DefaultTask() {
    @get:InputFiles
    @get:PathSensitive(PathSensitivity.RELATIVE)
    abstract val inputClasses: ConfigurableFileCollection
    @get:Classpath abstract val compilerClasspath: ConfigurableFileCollection
    @get:OutputDirectory abstract val outputDirectory: DirectoryProperty
    @get:Inject abstract val execOperations: ExecOperations

    @TaskAction fun generate() {
        val output = outputDirectory.file("projector-power.jar").get().asFile
        output.parentFile.mkdirs()
        execOperations.javaexec {
            classpath = compilerClasspath
            mainClass.set("com.android.tools.r8.D8")
            args("--min-api", "26", "--output", output.absolutePath)
            args(inputClasses.files.sortedBy { it.path }.map { it.absolutePath })
        }.assertNormalExitValue()
    }
}

android {
    namespace = "pl.screenreceiver.tv"
    compileSdk {
        version = release(37)
    }

    defaultConfig {
        applicationId = "pl.screenreceiver.tv"
        minSdk = 23
        targetSdk = 37
        versionCode = 1
        versionName = "1.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"

    }

    buildTypes {
        release {
            optimization {
                enable = true
                packageScope = setOf("androidx.**", "kotlin.**", "kotlinx.**")
            }
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }
    buildFeatures {
        compose = true
    }
}

val compileProjectorPower = tasks.register<JavaCompile>("compileProjectorPower") {
    source("src/powerControl/java")
    classpath = files()
    options.release.set(11)
    destinationDirectory.set(layout.buildDirectory.dir("projectorPower/classes"))
}
val powerBuildTools = android.buildToolsVersion
val dexProjectorPower = tasks.register<DexProjectorPower>("dexProjectorPower") {
    dependsOn(compileProjectorPower)
    inputClasses.from(compileProjectorPower.map { it.destinationDirectory.asFileTree.matching { include("**/*.class") } })
    compilerClasspath.from(androidComponents.sdkComponents.sdkDirectory.map { it.file("build-tools/$powerBuildTools/lib/d8.jar") })
    outputDirectory.set(layout.buildDirectory.dir("generated/projectorPowerAssets"))
}
androidComponents.onVariants { variant ->
    variant.sources.assets?.addGeneratedSourceDirectory(dexProjectorPower, DexProjectorPower::outputDirectory)
}

dependencies {
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.viewmodel)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.webrtc)
    implementation(libs.dadb)
    implementation(libs.androidx.tv.material)
    testImplementation(libs.junit)
    // Android supplies org.json at runtime; use its JVM implementation in local tests.
    testImplementation(libs.json)
    androidTestImplementation(platform(libs.androidx.compose.bom))
    androidTestImplementation(libs.androidx.compose.ui.test.junit4)
    debugImplementation(libs.androidx.compose.ui.test.manifest)
    debugImplementation(libs.androidx.compose.ui.tooling)
}
